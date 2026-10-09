# Audio effect latency API migration

`AudioEffect.GetLatencySamples` now accepts the accumulated input latency:

```csharp
public virtual int GetLatencySamples(int sampleRate, int inputLatency = 0)
```

This replaces `GetLatencySamples(int sampleRate)` and the internal
`GetOutputLatencySamples` hook. The optional parameter changes the CLR signature:
extension authors must update existing overrides and rebuild their assemblies.
One-argument calls remain valid in source and query the effect's own contribution.
There is no compatibility overload for the previous signature.

The result is the accumulated latency at the effect's output. Both the input and
output budgets are sample counts at the supplied positive `sampleRate`, measured
in their respective timelines. An effect with no delay returns `inputLatency`.
An additive effect adds its own delay, while an effect that changes playback speed
maps the upstream budget into its output timeline. Use conservative bounds for
animated parameters so the report agrees with the constructed graph's
`AudioNode.GetTotalLatencySamples(sampleRate)` before processing.

For an additive effect, replace an override that returned only its own delay with:

```csharp
public override int GetLatencySamples(int sampleRate, int inputLatency = 0)
{
    base.GetLatencySamples(sampleRate, inputLatency); // Validate both arguments.
    if (!IsEnabled)
        return inputLatency;

    int ownLatency = GetOwnDelaySamples(sampleRate);
    if (ownLatency < 0)
        throw new InvalidOperationException("The effect reported a negative delay.");

    long total = (long)inputLatency + ownLatency;
    return total >= int.MaxValue ? int.MaxValue : (int)total;
}
```

Validate `sampleRate > 0` and `inputLatency >= 0` even when the effect is disabled.
Disabled effects return `inputLatency` unchanged. Preserve `int.MaxValue` as an
unbounded or saturated budget, including when a speed mapping would reduce a
finite budget. Round fractional transformed latency up to a whole sample and
saturate arithmetic instead of overflowing.

`AudioEffectGroup` passes the accumulated result through enabled children in
order, including nested groups. At 48 kHz, an input budget of 200 samples followed
by a 10 ms Limiter and 50% Time Stretch reports `(200 + 480) / 0.5 = 1360` samples.
Reversing those effects reports `200 / 0.5 + 480 = 880` samples.

`AudioNode.GetLatencySamples(int sampleRate)` continues to report the node's own
processing delay. Its signature and the distinction between own, total, and drain
latency remain unchanged; node implementations must not include upstream latency
in their own-delay report.
