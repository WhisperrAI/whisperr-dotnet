using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Whisperr;

/// <summary>
/// Catalog item types (the <c>type</c> field).
/// </summary>
public static class CatalogItemTypes
{
    /// <summary>A product.</summary>
    public const string Product = "product";

    /// <summary>A subscription plan.</summary>
    public const string Plan = "plan";

    /// <summary>Content, including courses.</summary>
    public const string Content = "content";

    /// <summary>A physical location.</summary>
    public const string Location = "location";

    /// <summary>An offer.</summary>
    public const string Offer = "offer";
}

/// <summary>
/// Catalog availability values.
/// </summary>
public static class CatalogAvailability
{
    /// <summary>In stock.</summary>
    public const string Available = "available";

    /// <summary>Low stock, preorder or backorder.</summary>
    public const string Limited = "limited";

    /// <summary>Out of stock.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>Not known.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// One catalog item (whisperr-spec <c>schemas/catalog-item-v2.schema.json</c>).
/// Use the same <see cref="SourceItemId"/> you send as <c>product_id</c> in tracked events.
/// Null properties are not sent.
/// </summary>
public sealed class CatalogItem
{
    /// <summary>Your stable item id, at most 512 characters. Required.</summary>
    [JsonPropertyName("source_item_id")]
    public string SourceItemId { get; set; } = string.Empty;

    /// <summary>Item type, see <see cref="CatalogItemTypes"/>. Required.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = CatalogItemTypes.Product;

    /// <summary>v2 kind: product, content, course, plan, location or offer.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>Display name. Required.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Product page URL or deep link.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Availability, see <see cref="CatalogAvailability"/>. Required.</summary>
    [JsonPropertyName("availability")]
    public string Availability { get; set; } = CatalogAvailability.Unknown;

    /// <summary>When this item last changed in your system. Required for push; the newest write wins.</summary>
    [JsonPropertyName("updated_at")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Current price.</summary>
    [JsonPropertyName("price")]
    public CatalogPrice? Price { get; set; }

    /// <summary>Category slug.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Location id.</summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>Localized names by locale.</summary>
    [JsonPropertyName("localizations")]
    public IDictionary<string, CatalogLocalization>? Localizations { get; set; }

    /// <summary>Verified relationships to other items of this catalog.</summary>
    [JsonPropertyName("references")]
    public IList<CatalogReference>? References { get; set; }

    /// <summary>A read-only destination for the item.</summary>
    [JsonPropertyName("action")]
    public CatalogAction? Action { get; set; }

    /// <summary>Offer terms.</summary>
    [JsonPropertyName("offer_terms")]
    public CatalogOfferTerms? OfferTerms { get; set; }

    /// <summary>Your version string for this item.</summary>
    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }

    /// <summary>Main image URL, https only.</summary>
    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    /// <summary>Compare-at (regular) price in minor units.</summary>
    [JsonPropertyName("compare_at_minor")]
    public long? CompareAtMinor { get; set; }

    /// <summary>Lowest price over all variants, in minor units.</summary>
    [JsonPropertyName("price_min_minor")]
    public long? PriceMinMinor { get; set; }

    /// <summary>Highest price over all variants, in minor units.</summary>
    [JsonPropertyName("price_max_minor")]
    public long? PriceMaxMinor { get; set; }

    /// <summary>ISO 4217 currency of the prices above.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>True when any variant is in stock.</summary>
    [JsonPropertyName("any_variant_in_stock")]
    public bool? AnyVariantInStock { get; set; }

    /// <summary>Product type, at most 255 characters.</summary>
    [JsonPropertyName("product_type")]
    public string? ProductType { get; set; }

    /// <summary>Brand or vendor, at most 128 characters.</summary>
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    /// <summary>Collections or categories, at most 32.</summary>
    [JsonPropertyName("collections")]
    public IList<CatalogCollection>? Collections { get; set; }

    /// <summary>Tags, at most 32, each at most 64 characters.</summary>
    [JsonPropertyName("tags")]
    public IList<string>? Tags { get; set; }

    /// <summary>
    /// Only size, color, material, pattern, gender, age_group, condition,
    /// google_product_category, unit, flavor and scent; 512 bytes in total.
    /// </summary>
    [JsonPropertyName("extras")]
    public IDictionary<string, string>? Extras { get; set; }

    /// <summary>When the item was published.</summary>
    [JsonPropertyName("published_at")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>True for a gift card.</summary>
    [JsonPropertyName("gift_card")]
    public bool? GiftCard { get; set; }

    /// <summary>Variants, at most 100.</summary>
    [JsonPropertyName("variants")]
    public IList<CatalogVariant>? Variants { get; set; }
}

/// <summary>A price in integer minor units.</summary>
public sealed class CatalogPrice
{
    /// <summary>Integer minor units, e.g. 1999 for 19.99 USD. See <see cref="WhisperrMoney.ToMinorUnits(decimal, string)"/>.</summary>
    [JsonPropertyName("amount_minor")]
    public long AmountMinor { get; set; }

    /// <summary>ISO 4217, upper case.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>What the price means, when not the plain item price.</summary>
    [JsonPropertyName("meaning")]
    public string? Meaning { get; set; }
}

/// <summary>A collection or category the item belongs to.</summary>
public sealed class CatalogCollection
{
    /// <summary>Your stable collection id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>URL handle.</summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>Collection page URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>A variant. Whisperr stores variants only where a variant alert needs them.</summary>
public sealed class CatalogVariant
{
    /// <summary>Your stable variant id.</summary>
    [JsonPropertyName("variant_id")]
    public string VariantId { get; set; } = string.Empty;

    /// <summary>Variant title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Availability, see <see cref="CatalogAvailability"/>.</summary>
    [JsonPropertyName("availability")]
    public string Availability { get; set; } = CatalogAvailability.Unknown;

    /// <summary>Price in minor units.</summary>
    [JsonPropertyName("price_minor")]
    public long? PriceMinor { get; set; }

    /// <summary>Compare-at price in minor units.</summary>
    [JsonPropertyName("compare_at_minor")]
    public long? CompareAtMinor { get; set; }
}

/// <summary>A localized name.</summary>
public sealed class CatalogLocalization
{
    /// <summary>Localized name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Localized description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>A verified relationship to another item.</summary>
public sealed class CatalogReference
{
    /// <summary>Relation name.</summary>
    [JsonPropertyName("relation")]
    public string Relation { get; set; } = string.Empty;

    /// <summary>The other item's id.</summary>
    [JsonPropertyName("source_item_id")]
    public string SourceItemId { get; set; } = string.Empty;
}

/// <summary>A read-only destination (<c>view_item</c>).</summary>
public sealed class CatalogAction
{
    /// <summary>Action version, 1.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Action type, <c>view_item</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "view_item";

    /// <summary>What the action opens.</summary>
    [JsonPropertyName("target")]
    public CatalogActionTarget Target { get; set; } = new CatalogActionTarget();
}

/// <summary>The target of a <see cref="CatalogAction"/>.</summary>
public sealed class CatalogActionTarget
{
    /// <summary>Item type of the target.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = CatalogItemTypes.Product;

    /// <summary>Target id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Location id, for a location-bound target.</summary>
    [JsonPropertyName("location_id")]
    public string? LocationId { get; set; }
}

/// <summary>Offer terms.</summary>
public sealed class CatalogOfferTerms
{
    /// <summary>Offer kind.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Quantity to buy.</summary>
    [JsonPropertyName("buy_quantity")]
    public int BuyQuantity { get; set; }

    /// <summary>Quantity given free.</summary>
    [JsonPropertyName("free_quantity")]
    public int FreeQuantity { get; set; }

    /// <summary>True when a membership is required.</summary>
    [JsonPropertyName("requires_membership")]
    public bool RequiresMembership { get; set; }

    /// <summary>Who is eligible.</summary>
    [JsonPropertyName("eligibility")]
    public string Eligibility { get; set; } = string.Empty;

    /// <summary>Short summary.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}

/// <summary>One item to delete.</summary>
public sealed class CatalogDeletion
{
    /// <summary>The item's id.</summary>
    [JsonPropertyName("source_item_id")]
    public string SourceItemId { get; set; } = string.Empty;

    /// <summary>When the item was removed in your system. Defaults to the time of the call.</summary>
    [JsonPropertyName("deleted_at")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? DeletedAt { get; set; }
}
