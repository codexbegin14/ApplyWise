namespace ApplyWise.Web.Services.Wiso;

public sealed record WisoAction(string Label, string Url);
public sealed record WisoReply(string Message, IReadOnlyList<WisoAction> Actions);
public interface IWisoService
{
    Task<WisoReply> AskAsync(string userId, string question, CancellationToken cancellationToken = default);
}
