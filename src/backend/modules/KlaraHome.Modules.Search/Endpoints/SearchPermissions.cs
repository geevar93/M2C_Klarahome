namespace KlaraHome.Modules.Search.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another, so the two lists are kept in step by a test that
/// asserts every permission an endpoint asks for appears in the catalogue — the arrangement every
/// module since Media has used.
/// </para>
/// <para>
/// Three, and the split is the split between two jobs and one lever. Reading the query log and
/// editing the vocabulary are merchandising: they are decisions about what shoppers should find, and
/// the person who makes them is the person who decides what the store sells. Rebuilding the index is
/// neither — it is an operational action with a real cost, and it belongs to the people who run the
/// catalogue rather than to everyone who can add a synonym.
/// </para>
/// <para>
/// There is no read permission for the storefront surface at all. Searching is anonymous, as
/// browsing a shop is, and a permission on it would be a permission every shopper implicitly holds.
/// </para>
/// </remarks>
internal static class SearchPermissions
{
    /// <summary>
    /// Read what shoppers have searched for, including the queries that found nothing.
    /// </summary>
    /// <remarks>
    /// Separate from editing the vocabulary because the audiences differ. A buyer deciding what to
    /// stock next needs the zero-result report and has no business editing how the search behaves;
    /// support reads it to answer "why can't I find X".
    /// </remarks>
    public const string QueryRead = "search.query.read";

    /// <summary>
    /// Edit the store's synonyms and stop words.
    /// </summary>
    /// <remarks>
    /// A large amount of power over what shoppers find, and deliberately not an engineering
    /// permission: the person who notices that nobody finds "settee" is a merchandiser, and making
    /// them raise a ticket to fix it is how a store ends up with a search nobody trusts.
    /// </remarks>
    public const string VocabularyManage = "search.vocabulary.manage";

    /// <summary>
    /// Rebuild the index, and read its state.
    /// </summary>
    /// <remarks>
    /// The one action here with an operational cost — a full rebuild reads the whole catalogue — so
    /// it sits with the people who run the catalogue rather than with everyone who can add a synonym.
    /// The status read is bundled with it rather than with the query log, because the only reason to
    /// look at it is to decide whether to press the other button.
    /// </remarks>
    public const string IndexManage = "search.index.manage";
}
