namespace TrelloNotifier.Models;

public enum CardImportState
{
    Ready,
    Invalid,
    Excluded,
    Creating,
    Created,
    Rejected,
    Uncertain
}
