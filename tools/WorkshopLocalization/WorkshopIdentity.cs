using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WorkshopLocalization;

internal sealed record CreationReceipt(int Version, string Owner, string? ItemId, bool Completed);

/// <summary>Mutable identity and retry state live outside the frozen release.</summary>
internal sealed class WorkshopIdentity(string itemDirectory, string stateDirectory)
{
    private string ReceiptPath => Path.Combine(stateDirectory, "creation.json");

    private CreationReceipt? ReadReceipt()
    {
        if (!File.Exists(ReceiptPath)) return null;
        var receipt = JsonSerializer.Deserialize<CreationReceipt>(File.ReadAllText(ReceiptPath), Publisher.JsonOptions);
        if (receipt == null || receipt.Version != 1 || !ValidId(receipt.Owner)
            || (receipt.ItemId != null && !ValidId(receipt.ItemId)) || (receipt.Completed && receipt.ItemId == null))
            throw new InvalidDataException("Invalid Workshop creation receipt.");
        return receipt;
    }

    private static bool ValidId(string? text) => ulong.TryParse(text, NumberStyles.None,
        CultureInfo.InvariantCulture, out var id) && id != 0;

    public PreparedRelease Inspect(PreparedRelease release, bool fullRelease)
    {
        var receipt = ReadReceipt();
        var external = ListingFiles.ReadItemId(itemDirectory, required: false);
        var saved = receipt?.ItemId is string text ? ulong.Parse(text, CultureInfo.InvariantCulture) : (ulong?)null;
        if (external != null && saved != null && external != saved)
            throw new InvalidDataException("Saved Workshop item IDs disagree.");
        var item = external ?? saved ?? 0;
        if (release.ItemId != 0 && release.ItemId != item)
            throw new InvalidDataException("Workshop item ID changed after preparation. Run ./prepare.sh again.");
        if (release.ItemId == 0 && item != 0 && receipt == null)
            throw new InvalidDataException("An item ID was added after preparation. Run ./prepare.sh again.");
        if ((release.ItemId == 0 || receipt is { Completed: false })
            && release.Settings.GetProperty("visibility").GetString() != "private")
            throw new InvalidDataException("The first release must use private visibility. Run ./prepare.sh again.");
        if (item == 0 && !fullRelease)
            throw new InvalidOperationException("Run a full ./release.sh to create the item before publishing a single language or previews.");
        if (item == 0 && receipt != null)
            throw new InvalidOperationException("A previous item creation has an unknown outcome. Check your Workshop items, "
                + "save the returned ID to workshop/mod_id.txt, and retry. If Steam confirms no item was created, "
                + "remove workshop/.release-state/creation.json before retrying.");
        return release with { ItemId = item };
    }

    public PreparedRelease Resolve(PreparedRelease release, IWorkshopClient client, bool fullRelease, Action<string> log)
    {
        release = Inspect(release, fullRelease);
        var receipt = ReadReceipt();
        var owner = client.UserId.ToString(CultureInfo.InvariantCulture);
        if (receipt != null && receipt.Owner != owner)
            throw new InvalidOperationException("Sign in to the Steam account that created this Workshop item.");
        if (release.ItemId == 0)
        {
            // Persist intent before the request. An interrupted/ambiguous create must never be retried blindly.
            receipt = new CreationReceipt(1, owner, null, false);
            Publisher.AtomicWrite(ReceiptPath, receipt);
            var created = client.CreateItem();
            if (created.ItemId == 0) throw new InvalidDataException("Steam returned an invalid new item ID.");
            log($"Created Workshop item {created.ItemId}; the first release uses private visibility.");
            release = release with { ItemId = created.ItemId };
            receipt = receipt with { ItemId = created.ItemId.ToString(CultureInfo.InvariantCulture) };
            Publisher.AtomicWrite(ReceiptPath, receipt);
            SaveItemId(created.ItemId);
            if (created.NeedsLegalAgreement)
                throw new InvalidOperationException($"Item ID saved. Accept the Workshop agreement at "
                    + $"https://steamcommunity.com/sharedfiles/filedetails/?id={created.ItemId} and rerun ./release.sh.");
        }
        else
        {
            if (receipt is { ItemId: null })
                Publisher.AtomicWrite(ReceiptPath, receipt with { ItemId = release.ItemId.ToString(CultureInfo.InvariantCulture) });
            SaveItemId(release.ItemId);
        }
        return release;
    }

    private void SaveItemId(ulong item)
    {
        var path = Path.Combine(itemDirectory, "mod_id.txt");
        if (File.Exists(path)) return; // Inspect already checked its value.
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(Encoding.UTF8.GetBytes(item.ToString(CultureInfo.InvariantCulture) + "\n"));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path); // Never replace another identity.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Complete()
    {
        if (ReadReceipt() is { Completed: false } receipt)
            Publisher.AtomicWrite(ReceiptPath, receipt with { Completed = true });
    }
}
