using System.Security.Cryptography;
using System.Text;

namespace Beutl.AgentToolkit.Live;

public sealed record AgentHostIdentityProof(string InstanceId, string Challenge, string Proof);

public sealed class AgentHostInstanceAuthentication(string token, string instanceId)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(token);

    public string InstanceId => instanceId;

    public static string CreateChallenge() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static bool IsValidChallenge(string? challenge)
        => challenge is { Length: 64 } && challenge.All(Uri.IsHexDigit);

    public AgentHostIdentityProof CreateProof(string challenge)
        => new(InstanceId, challenge, Convert.ToHexString(ComputeProof(InstanceId, challenge)));

    public bool VerifyProof(string expectedInstanceId, string challenge, AgentHostIdentityProof? proof)
        => proof?.InstanceId == expectedInstanceId && proof.Challenge == challenge
           && Matches(proof.Proof, ComputeProof(expectedInstanceId, challenge));

    // Separate the proof and forwarding purposes. The public challenge endpoint must never be
    // an oracle for a credential. A captured forwarding credential works only for this host ID,
    // even if the listener is replaced between the proof and the authenticated request.
    public string CreateForwardToken(string targetInstanceId)
        => Convert.ToHexString(Compute("beutl-agent-host/forward/" + targetInstanceId));

    public bool IsForwardToken(string provided)
        => Matches(provided, Compute("beutl-agent-host/forward/" + InstanceId));

    private byte[] ComputeProof(string targetInstanceId, string challenge)
        => Compute("beutl-agent-host/identity/" + targetInstanceId + "/" + challenge);

    private byte[] Compute(string message) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message));

    private static bool Matches(string? provided, byte[] expected)
        => IsValidChallenge(provided)
           && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(provided!), expected);
}
