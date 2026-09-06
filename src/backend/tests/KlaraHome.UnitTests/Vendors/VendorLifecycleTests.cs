using KlaraHome.Modules.Vendors.Application;
using KlaraHome.Modules.Vendors.Domain;

namespace KlaraHome.UnitTests.Vendors;

/// <summary>
/// The onboarding state machine (docs/02-domain-model.md §5).
/// </summary>
/// <remarks>
/// A transition table is exactly the kind of algorithm the build sprint's rule 1 says to test while
/// writing it: the whole thing is 36 cells, the cost of asserting them is a few lines, and a
/// missing edge is invisible in review — nobody reads a <c>switch</c> and notices the case that is
/// not there.
/// </remarks>
public sealed class VendorLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every transition the life cycle allows, and nothing else.</summary>
    private static readonly (VendorStatus From, VendorStatus To)[] Allowed =
    [
        (VendorStatus.Applied, VendorStatus.UnderReview),
        (VendorStatus.Applied, VendorStatus.Offboarded),
        (VendorStatus.UnderReview, VendorStatus.Applied),
        (VendorStatus.UnderReview, VendorStatus.Approved),
        (VendorStatus.UnderReview, VendorStatus.Offboarded),
        (VendorStatus.Approved, VendorStatus.Active),
        (VendorStatus.Approved, VendorStatus.Offboarded),
        (VendorStatus.Active, VendorStatus.Suspended),
        (VendorStatus.Active, VendorStatus.Offboarded),
        (VendorStatus.Suspended, VendorStatus.Active),
        (VendorStatus.Suspended, VendorStatus.Offboarded),
    ];

    [Fact]
    public void The_transition_table_allows_exactly_the_declared_edges_and_no_others()
    {
        foreach (var from in Enum.GetValues<VendorStatus>())
        {
            foreach (var to in Enum.GetValues<VendorStatus>())
            {
                var expected = Array.Exists(Allowed, edge => edge.From == from && edge.To == to);

                Assert.Equal(expected, Vendor.IsTransitionAllowed(from, to));
            }
        }
    }

    [Fact]
    public void A_seller_cannot_move_to_the_state_they_are_already_in()
    {
        foreach (var status in Enum.GetValues<VendorStatus>())
        {
            Assert.False(Vendor.IsTransitionAllowed(status, status));
        }
    }

    [Fact]
    public void Offboarded_is_terminal()
    {
        foreach (var to in Enum.GetValues<VendorStatus>())
        {
            Assert.False(Vendor.IsTransitionAllowed(VendorStatus.Offboarded, to));
        }
    }

    [Fact]
    public void A_refused_transition_leaves_the_seller_untouched()
    {
        var vendor = Applicant();

        Assert.False(vendor.TransitionTo(VendorStatus.Active, Now));
        Assert.Equal(VendorStatus.Applied, vendor.Status);
        Assert.Null(vendor.OnboardedAt);
    }

    [Fact]
    public void The_onboarding_date_is_set_on_the_first_activation_and_never_moved()
    {
        var vendor = Activated();
        var first = vendor.OnboardedAt;

        Assert.NotNull(first);

        Assert.True(vendor.TransitionTo(VendorStatus.Suspended, Now.AddDays(30), "Late dispatches."));
        Assert.True(vendor.TransitionTo(VendorStatus.Active, Now.AddDays(60)));

        // A seller who was suspended and reinstated has not joined the marketplace twice, and their
        // tenure is what a loyalty-based commission tier would read.
        Assert.Equal(first, vendor.OnboardedAt);
    }

    [Fact]
    public void A_reason_is_recorded_on_a_suspension_and_cleared_when_they_come_back()
    {
        var vendor = Activated();

        vendor.TransitionTo(VendorStatus.Suspended, Now, "  Three undelivered orders.  ");
        Assert.Equal("Three undelivered orders.", vendor.StatusReason);

        vendor.TransitionTo(VendorStatus.Active, Now.AddDays(1));
        Assert.Null(vendor.StatusReason);
    }

    [Fact]
    public void A_seller_only_trades_while_they_are_active()
    {
        var vendor = Applicant();
        Assert.False(vendor.IsTrading);

        vendor.TransitionTo(VendorStatus.UnderReview, Now);
        vendor.TransitionTo(VendorStatus.Approved, Now);

        // Approved is not trading. The checks passed; letting them sell is a separate decision.
        Assert.False(vendor.IsTrading);

        vendor.TransitionTo(VendorStatus.Active, Now);
        Assert.True(vendor.IsTrading);

        vendor.TransitionTo(VendorStatus.Suspended, Now, "why");
        Assert.False(vendor.IsTrading);
    }

    [Fact]
    public void The_required_documents_follow_the_legal_form()
    {
        // A [Theory] would put the internal enum in a public method signature, which does not
        // compile across the InternalsVisibleTo boundary — so the cases are a table instead.
        (VendorBusinessType Type, bool NeedsIncorporation)[] cases =
        [
            (VendorBusinessType.Individual, false),
            (VendorBusinessType.SoleProprietorship, false),
            (VendorBusinessType.PrivateLimited, true),
            (VendorBusinessType.Trust, true),
        ];

        foreach (var (type, needsIncorporation) in cases)
        {
            var vendor = Vendor.Apply("VND-000001", "Acme Furnishings", "Acme", "acme", type);
            var required = VendorReadinessService.RequiredDocuments(vendor);

            // Everybody produces a PAN, whatever they trade as: it is what a TDS return is filed
            // against.
            Assert.Contains(KycDocumentType.Pan, required);
            Assert.Equal(needsIncorporation, required.Contains(KycDocumentType.IncorporationCertificate));

            // A seller below the GST threshold is not required to register, so nothing demands the
            // certificate until they claim one.
            Assert.DoesNotContain(KycDocumentType.Gstin, required);
        }
    }

    [Fact]
    public void A_seller_who_claims_a_gstin_must_produce_the_certificate_for_it()
    {
        var vendor = Applicant();

        vendor.DescribeBusiness(
            "Acme Furnishings",
            VendorBusinessType.PrivateLimited,
            "ABCDE1234F",
            "27ABCDE1234F1Z5",
            new RegisteredAddress());

        var required = VendorReadinessService.RequiredDocuments(vendor);

        Assert.Contains(KycDocumentType.Gstin, required);
    }

    private static Vendor Applicant()
        => Vendor.Apply("VND-000001", "Acme Furnishings", "Acme", "acme", VendorBusinessType.PrivateLimited);

    private static Vendor Activated()
    {
        var vendor = Applicant();

        vendor.TransitionTo(VendorStatus.UnderReview, Now);
        vendor.TransitionTo(VendorStatus.Approved, Now);
        vendor.TransitionTo(VendorStatus.Active, Now);

        return vendor;
    }
}
