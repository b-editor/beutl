# Consistency Analysis

The specification deliberately separates product analytics from existing operational activity sources and logging. This prevents installation identifiers from becoming a metric label or a broad diagnostic attribute. The static manifest and release hash requirements preserve the ability to measure trusted extension adoption without making a runtime plugin telemetry API public.
