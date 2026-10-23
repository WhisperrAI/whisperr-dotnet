using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Whisperr;

/// <summary>An item Whisperr refused. The rest of the batch was still written.</summary>
public sealed class CatalogRejection
{
    /// <summary>Index into the list you passed (not into one chunk).</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>The refused item's id.</summary>
    [JsonPropertyName("source_item_id")]
    public string SourceItemId { get; set; } = string.Empty;

    /// <summary>Reject code, e.g. <c>invalid_url</c>, <c>url_host_not_allowed</c>, <c>invalid_price</c>.</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable reason.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>The result of <see cref="WhisperrCatalogClient.UpsertAsync"/>, summed over all chunks.</summary>
public sealed class CatalogUpsertResult
{
    /// <summary>Items Whisperr accepted.</summary>
    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    /// <summary>Accepted items that changed what Whisperr stores.</summary>
    [JsonPropertyName("applied")]
    public int Applied { get; set; }

    /// <summary>Accepted items that were already current.</summary>
    [JsonPropertyName("unchanged")]
    public int Unchanged { get; set; }

    /// <summary>Accepted items older than what Whisperr stores, so not applied.</summary>
    [JsonPropertyName("ignored_older")]
    public int IgnoredOlder { get; set; }

    /// <summary>Refused items, in input order.</summary>
    [JsonPropertyName("rejected")]
    public List<CatalogRejection> Rejected { get; set; } = new List<CatalogRejection>();
}

/// <summary>The result of <see cref="WhisperrCatalogClient.DeleteAsync(IEnumerable{string}, DateTimeOffset?, System.Threading.CancellationToken)"/>, summed over all chunks.</summary>
public sealed class CatalogDeleteResult
{
    /// <summary>Items deleted.</summary>
    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }

    /// <summary>Deletions older than the stored item, so not applied.</summary>
    [JsonPropertyName("ignored_older")]
    public int IgnoredOlder { get; set; }

    /// <summary>Ids Whisperr does not hold.</summary>
    [JsonPropertyName("not_found")]
    public int NotFound { get; set; }

    /// <summary>Refused deletions, in input order.</summary>
    [JsonPropertyName("rejected")]
    public List<CatalogRejection> Rejected { get; set; } = new List<CatalogRejection>();
}

/// <summary>An open full-sync snapshot.</summary>
public sealed class CatalogSnapshot
{
    /// <summary>Pass this to <see cref="WhisperrCatalogClient.CompleteSnapshotAsync"/>.</summary>
    [JsonPropertyName("snapshot_id")]
    public string SnapshotId { get; set; } = string.Empty;

    /// <summary>When the snapshot started.</summary>
    [JsonPropertyName("started_at")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? StartedAt { get; set; }
}

/// <summary>The result of <see cref="WhisperrCatalogClient.CompleteSnapshotAsync"/>.</summary>
public sealed class CatalogSnapshotResult
{
    /// <summary>Items removed because the snapshot did not include them.</summary>
    [JsonPropertyName("tombstoned")]
    public int Tombstoned { get; set; }

    /// <summary>The item count you sent.</summary>
    [JsonPropertyName("item_count")]
    public long ItemCount { get; set; }

    /// <summary>Catalog state, e.g. <c>ready</c>.</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}

/// <summary>The result of <see cref="WhisperrCatalogClient.HeartbeatAsync"/>.</summary>
public sealed class CatalogHeartbeatResult
{
    /// <summary>True when the stored items were marked current.</summary>
    [JsonPropertyName("refreshed")]
    public bool Refreshed { get; set; }

    /// <summary>Why not, e.g. <c>count_mismatch</c>.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>The live item count Whisperr holds, on a mismatch.</summary>
    [JsonPropertyName("stored_count")]
    public long? StoredCount { get; set; }
}

/// <summary>The health of the pushed catalog source.</summary>
public sealed class CatalogSourceHealth
{
    /// <summary>Connection id.</summary>
    [JsonPropertyName("connection_id")]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Provider, <c>custom_code</c> for push.</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>connector, feed, push or push_feed.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>missing, pending_confirmation, awaiting_first_sync, ready, ready_with_exclusions, stale, failing or disabled.</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>Live items.</summary>
    [JsonPropertyName("item_count")]
    public long ItemCount { get; set; }

    /// <summary>Live items in stock.</summary>
    [JsonPropertyName("available_count")]
    public long AvailableCount { get; set; }

    /// <summary>Items waiting for the safety check.</summary>
    [JsonPropertyName("unclassified_count")]
    public long UnclassifiedCount { get; set; }

    /// <summary>Items excluded from messages.</summary>
    [JsonPropertyName("excluded_count")]
    public long ExcludedCount { get; set; }

    /// <summary>How long an item counts as current after its last update or heartbeat.</summary>
    [JsonPropertyName("freshness_seconds")]
    public long FreshnessSeconds { get; set; }

    /// <summary>The newest item freshness.</summary>
    [JsonPropertyName("newest_fresh_as_of")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? NewestFreshAsOf { get; set; }

    /// <summary>The last successful write.</summary>
    [JsonPropertyName("last_success_at")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>The last push.</summary>
    [JsonPropertyName("last_push_at")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? LastPushAt { get; set; }

    /// <summary>The last heartbeat.</summary>
    [JsonPropertyName("last_heartbeat_at")]
    [JsonConverter(typeof(CatalogTimeConverter))]
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    /// <summary>The last write error code.</summary>
    [JsonPropertyName("last_error")]
    public string? LastError { get; set; }

    /// <summary>Open issues.</summary>
    [JsonPropertyName("issue_count")]
    public long IssueCount { get; set; }

    /// <summary>The last URL host that was refused.</summary>
    [JsonPropertyName("rejected_host")]
    public string? RejectedHost { get; set; }

    /// <summary>The suggested repair action.</summary>
    [JsonPropertyName("repair")]
    public string Repair { get; set; } = string.Empty;

    /// <summary>Dashboard path for the repair.</summary>
    [JsonPropertyName("repair_path")]
    public string RepairPath { get; set; } = string.Empty;
}
