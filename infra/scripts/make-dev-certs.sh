#!/usr/bin/env bash
# =============================================================================
# Klara Home - a locally-trusted certificate for the dev stack
# =============================================================================
# Traefik serves its own self-signed certificate unless it is given one, and a
# self-signed certificate is not merely a browser warning to click past: the
# storefront and the admin app call the API with XHR, and an XHR to a host whose
# certificate the browser does not trust is refused with no interstitial, no
# usable error, and every symptom of a broken application.
#
# This is what mkcert does, written out with openssl so it needs no extra tool:
# a local certificate authority, and one leaf certificate covering every
# hostname the dev stack answers on. Trusting the CA once makes all of them
# trusted, including the ones added later.
#
#   ./infra/scripts/make-dev-certs.sh
#
# Then trust infra/traefik/certs/dev-ca.pem - the script prints how - and
# restart Traefik. Nothing here is committed: infra/traefik/certs/ is git-ignored
# and a certificate is generated per machine, never shared.
# =============================================================================
set -euo pipefail

# Git Bash on Windows rewrites any argument that looks like a Unix path into a
# Windows one, which turns openssl's "/CN=..." subject into "C:/Program
# Files/Git/CN=..." and fails with a message about the subject format rather
# than about the translation.
#
# Excluded by prefix rather than switched off wholesale: the conversion is what
# turns the /c/... paths below into the C:\... ones a Windows openssl can
# actually open, so disabling it entirely trades this failure for a stranger
# one where every -keyout writes nowhere. Ignored on Linux and macOS.
export MSYS2_ARG_CONV_EXCL='/CN='

# openssl scribbles key-generation progress dots across stderr, so it is
# captured rather than shown - and printed in full when the command fails,
# because the alternative is a script that exits 1 with nothing on the screen.
openssl_quiet() {
    local log status
    log="$(mktemp)"
    openssl "$@" >"$log" 2>&1 && status=0 || status=$?
    [ "$status" -eq 0 ] || cat "$log" >&2
    rm -f "$log"
    return "$status"
}

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CERT_DIR="$REPO_ROOT/infra/traefik/certs"
DOMAIN="${DEV_DOMAIN:-klarahome.localhost}"

# Ten years on the CA and two on the leaf. A development certificate that
# expires is a morning lost to a failure that looks like anything but an expiry.
CA_DAYS=3650
LEAF_DAYS=730

mkdir -p "$CERT_DIR"

if [ -f "$CERT_DIR/dev-ca.pem" ] && [ "${REGENERATE_CA:-}" != "1" ]; then
    echo "Reusing the existing CA at infra/traefik/certs/dev-ca.pem."
    echo "(REGENERATE_CA=1 forces a new one - you would have to trust it again.)"
else
    echo "Generating the local certificate authority..."
    openssl_quiet req -x509 -newkey rsa:4096 -sha256 -days "$CA_DAYS" -nodes \
        -keyout "$CERT_DIR/dev-ca-key.pem" \
        -out "$CERT_DIR/dev-ca.pem" \
        -subj "/CN=Klara Home local development CA/O=Klara Home" \
        -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
        -addext "keyUsage=critical,keyCertSign,cRLSign"
fi

echo "Generating the leaf certificate for $DOMAIN..."

# The wildcard covers api., admin., mail., minio., s3., img. and traefik. in one
# entry; the apex is listed separately because a wildcard does not match it.
# 127.0.0.1 is an IP SAN rather than a DNS name - a certificate that names it as
# DNS is rejected by every modern client.
openssl_quiet req -newkey rsa:2048 -sha256 -nodes \
    -keyout "$CERT_DIR/local-key.pem" \
    -out "$CERT_DIR/local.csr" \
    -subj "/CN=$DOMAIN/O=Klara Home"

cat > "$CERT_DIR/local.ext" <<EXT
basicConstraints=CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:$DOMAIN,DNS:*.$DOMAIN,DNS:localhost,IP:127.0.0.1,IP:::1
EXT

openssl_quiet x509 -req -in "$CERT_DIR/local.csr" \
    -CA "$CERT_DIR/dev-ca.pem" -CAkey "$CERT_DIR/dev-ca-key.pem" -CAcreateserial \
    -out "$CERT_DIR/local-cert.pem" -days "$LEAF_DAYS" -sha256 \
    -extfile "$CERT_DIR/local.ext"

rm -f "$CERT_DIR/local.csr" "$CERT_DIR/local.ext"

# Traefik reads the certificate from the dynamic directory, which it watches, so
# this takes effect without a restart once the file is in place.
if [ ! -f "$REPO_ROOT/infra/traefik/dynamic/certs.yml" ]; then
    cp "$REPO_ROOT/infra/traefik/dynamic/certs.yml.example" \
       "$REPO_ROOT/infra/traefik/dynamic/certs.yml"
    echo "Enabled infra/traefik/dynamic/certs.yml."
fi

echo
echo "Done. The certificate covers $DOMAIN, *.$DOMAIN, localhost and 127.0.0.1."
echo
echo "Trust the CA once - Windows, in an ELEVATED PowerShell:"
echo
echo "    certutil -addstore -f Root \"$(cygpath -w "$CERT_DIR/dev-ca.pem" 2>/dev/null || echo "$CERT_DIR/dev-ca.pem")\""
echo
echo "Then restart the browser completely. Firefox keeps its own trust store and"
echo "needs the same file imported under Settings > Privacy > Certificates."
