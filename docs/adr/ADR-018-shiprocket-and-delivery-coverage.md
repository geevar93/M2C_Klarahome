# ADR-018 — Shiprocket is the named v1 courier, and delivery coverage is a policy rather than a zone

- **Status:** ✅ Accepted
- **Raised at:** Step 16 boundary, by the User: *"We are going with Shiprocket for fulfilment …
  the shipping vendor should be switchable … validate the pin code entry using Shiprocket's API …
  local delivery restriction should be configurable, by default restrict to Hyderabad."*
- **Supersedes:** —
- **Related:** ADR-014 (a paid provider's absence is a runtime switch, not a build-time one),
  ADR-017 (a channel with no provider is suppressed, not failed)

---

## Context

Step 16 built the Shipping module against `IShippingProvider` with two adapters: an
`AggregatorShippingProvider` written to the *shape* of a Shiprocket-class API, and a
`ManualShippingProvider` for a deployment with no logistics account at all. Neither names a courier.
`08-integrations.md` §2 recommended "an aggregator" and §7 recorded the Logistics row as `TBC`.

The client has now chosen **Shiprocket**, and has asked for two things the module does not yet do:

1. **A real PIN-code check.** Today the storefront answers "can you deliver here" from a cache that,
   with no aggregator configured, has never been filled — so it answers optimistically for every
   PIN code in India. That is the right default for an unconfigured deployment and the wrong
   behaviour for a configured one.
2. **A delivery area.** The store trades in **Hyderabad** and will refuse orders elsewhere until it
   does not. This is a commercial decision that will change, possibly several times, and must not
   require a deployment to change.

Underneath both sits a design question Step 16 deferred: the adapter registry keys on the constant
`"aggregator"`, so this platform can hold exactly one aggregator, and a parcel booked by it records
`provider = "aggregator"` rather than who actually has it. Naming Shiprocket by editing that
constant would name it in the type system, which is precisely what the interface exists to avoid.

## Decision

### 1. Adapters are keyed by the courier they are, and the key is configuration

`ShippingProviders.Aggregator` is retired as a *selector*. Every adapter publishes its own key —
`shiprocket`, `manual`, and whatever comes next — and `Shipping:Provider` names which one books new
consignments. `ShippingProviderRegistry` resolves that key through DI; a blank or unknown key
resolves to `manual`, which is still always present and still always able to record a parcel.

`AggregatorShippingProvider` is renamed `ShiprocketShippingProvider` and finished against
Shiprocket's actual API rather than its class of API. Adding Delhivery later is one class, one key
and one configuration value — no caller changes, because no caller has ever named a courier.

**A shipment records the adapter that booked it**, by its real key. Switching a store from
Shiprocket to another courier therefore leaves the parcels already in flight talking to Shiprocket:
tracking, labels, cancellation and cash reconciliation for those follow the key on the row, not the
key in configuration. This is the whole reason the seam is worth having, and it only works if the
row remembers the truth.

### 2. "Can a courier reach it" and "will we deliver there" are two questions

They have different owners, different failure modes and different messages, and conflating them
produces a shop that cannot explain itself.

- **Serviceability** is Shiprocket's answer, obtained from
  `GET /v1/external/courier/serviceability/`, written to `shipping.serviceability_cache`, and read
  from that cache by every request path. The storefront still never calls a courier — the rule from
  `08-integrations.md` §2 is unchanged and is now load-bearing rather than theoretical.
- **Coverage** is *this store's* policy: the set of destinations it is willing to sell to. It lives
  in a public settings section, is edited in admin, and takes effect immediately.

An address must pass **both**. A PIN code outside the coverage area is refused with
`DELIVERY_AREA_NOT_COVERED`; one inside the area that Shiprocket cannot reach is refused with
`PINCODE_NOT_SERVICEABLE`. An operator reading a lost-order report can tell the two apart, which is
the point: the first is a decision they can reverse in a settings screen, and the second is not.

### 3. Coverage is a policy, checked at every gate — not a shipping zone

A shipping zone with no rate would also, technically, prevent an order: the quote would find no
rule and the checkout would fail. That is a bad way to express a trading decision. A zone is a
*pricing* construct, its absence is indistinguishable from a misconfigured rate card, and the
failure surfaces at the last possible moment — after the shopper has entered an address, chosen a
payment method and, in the worst arrangement, paid.

Coverage is therefore checked at each of the five points where a destination becomes known, and
each one refuses with the same named error:

1. `GET /store/shipping/serviceability/{pincode}` — the PDP and cart check, before anything exists.
2. Address selection on a checkout session.
3. Cart validation, as a blocking issue that names the address.
4. `IShippingOptions` — an uncovered destination is offered no service, so checkout cannot advance.
5. `place-order`, as the last defence.

Checking once would be cheaper and would eventually take money for an order the store cannot ship.

### 4. The default is Hyderabad, and the default is data

`DeliveryCoverageSettings` ships enabled, allowing the cities **Hyderabad** and **Secunderabad** and
the PIN-code prefix **`500`**. The neighbouring `501` and `502` ranges — Rangareddy and Sangareddy,
the outer districts of the metropolitan region — are **excluded by default and are one edit away**,
because "Hyderabad" means different things to a postal service and to a delivery operation, and
guessing the larger meaning would have the store promising deliveries it has not decided to make.

A PIN code's city is read from `platform.pincodes`, which is seeded reference data and authoritative;
where the platform has no row, the city Shiprocket returns for that PIN code is used and cached.
Turning coverage **off** restores national trading, which is what a deployment that grows out of one
city does, and it is a settings change rather than a release.

## Consequences

- **Good:** the courier is named in configuration and nowhere else. The client can move to Delhivery
  or to a direct contract without this platform learning a new word for "book a parcel".
- **Good:** a shopper in Vijayawada is told on the product page, not after entering their card
  details, and is told *why* in a sentence the operator wrote.
- **Good:** the coverage rule is one settings row. Opening Bengaluru is typing `560` into a field.
- **Good:** the serviceability cache stops being decorative. With Shiprocket configured, an optimistic
  answer for an unasked PIN code is corrected within one refresh cycle rather than never.
- **Cost:** two rejection reasons where the storefront previously had none, and both need copy, a
  storefront treatment and a place in the checkout error map. Steps 24 and 25 inherit that.
- **Cost:** `shipping.serviceability_cache` gains `city` and `state`, which is a schema change to a
  table Step 16 has already migrated. It is additive and nullable.
- **Cost:** Shiprocket authenticates webhooks with a shared secret in an `x-api-key` header rather
  than an HMAC over the body. The verification seam must accommodate both, and the constant-time
  comparison matters more, not less, because a header secret is the entire proof of origin.
- **Watch:** the optimistic-on-miss rule in `ServiceabilityService` is correct for serviceability and
  would be wrong for coverage. Coverage has no cache and no miss — it is computed from settings and
  a PIN code, and it refuses by default when it cannot decide.
