# Historical rendering migration inventory

Captured at `38e9948a94b4e898018fae674d9addbedf77ce97`. This is audit evidence,
not a required layout or method count for future code. Permanent tests continue
to check recording signatures and prohibited renderer APIs across all discovered
source files, including newly added render nodes and tests.

## Production at the captured commit

| File | Declared Process overrides |
| --- | ---: |
| `src/Beutl.Engine/Graphics/AudioVisualizers/AudioVisualizerRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/DrawableGroup.cs` | 3 |
| `src/Beutl.Engine/Graphics/Particles/ParticleRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/BlendModeRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/ClearRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/CompleteTargetRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/ContainerRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/DrawBackdropRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/EllipseRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/GeometryClipRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/GeometryRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/ImageSourceRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/LayerRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/MemoryNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/OpacityMaskRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/OpacityRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/PushRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/RectClipRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/RectangleRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/ReferencesChildRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/SnapshotBackdropRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/TextRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/TransformRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs` | 1 |
| `src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs` | 1 |
| `src/Beutl.NodeGraph/NodeGraphFilterEffectRenderNode.cs` | 1 |
| `src/Beutl.NodeGraph/Nodes/FilterEffectInputRenderNode.cs` | 1 |
| `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs` | 1 |

## Tests at the captured commit

| File | Declared Process overrides |
| --- | ---: |
| `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs` | 6 |
| `tests/Beutl.Graphics3DTests/GpuPassFusion3DBoundaryTests.cs` | 1 |
| `tests/Beutl.Graphics3DTests/ShaderDescriptionSpirvEquivalenceTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/CapturedResourceBorrowContractTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/DeclaredPlannerTraitContractTests.cs` | 2 |
| `tests/Beutl.PublicApiContractTests/DeclaredResourceAddressingContractTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/DetachedResourceAuthoringContractTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/FilterEffectCompatibilityContractTests.cs` | 5 |
| `tests/Beutl.PublicApiContractTests/RenderNodeContextMetadataContractTests.cs` | 3 |
| `tests/Beutl.PublicApiContractTests/GeometryAuthoringContractTests.cs` | 2 |
| `tests/Beutl.PublicApiContractTests/OpaqueOutputPublicationContractTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/OpaqueSourceStateContractTests.cs` | 2 |
| `tests/Beutl.PublicApiContractTests/OrphanedTargetEffectContractTests.cs` | 2 |
| `tests/Beutl.PublicApiContractTests/PaintedSourceAuthoringContractTests.cs` | 3 |
| `tests/Beutl.PublicApiContractTests/RenderDescriptionPublicSurfaceContractTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/RenderNodeAuthoringContractTests.cs` | 2 |
| `tests/Beutl.PublicApiContractTests/RenderNodeRendererContractTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/RenderScaleMappingContractTests.cs` | 7 |
| `tests/Beutl.PublicApiContractTests/ShaderAuthoringContractTests.cs` | 1 |
| `tests/Beutl.PublicApiContractTests/TargetAuthoringContractTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ShaderDescriptionOffsetMetadataTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/BrushIntermediateAllocationIntentTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/DegradedPreviewCachePurityTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/OutputIdentityFanOutCostTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/ContributeValuesCacheHitExecutionTests.cs` | 4 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderCacheIdentityChannelTests.cs` | 5 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderCacheResolutionTests.cs` | 4 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/ShaderRequestScaleIdentityTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/MetadataCallbackIdentityTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderNodeCacheHelperTest.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/StructuralAndProgramCacheTests.cs` | 6 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ContainerRenderNodeTest.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/DeviceBufferBudgetTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/DirectSkiaFilterReplayTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/EmptyOpaquePublishTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/EngineResourceIdentityRoutingTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/DeferredCallbackFailureTests.cs` | 7 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/NestedTargetAndCleanupFailureTests.cs` | 16 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RecordingAndPlanningFailureTests.cs` | 6 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RenderNodeRendererLifetimeTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/ShaderAndAllocationFailureTests.cs` | 4 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/CrossNodeShaderFusionTests.cs` | 5 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/ExecutionIslandAuthorityTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/FusionBoundaryExecutionTestSupport.cs` | 6 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/ShaderFallbackTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/DirectBlurFiniteOutputTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ExecutionIslandOrderTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionFeature003RegressionTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionScaleRegionTests.cs` | 7 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/LosslessCompositeCoverageTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ShaderMatrixUniformTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/TargetCaptureValueWrapperTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/WholeSourceFragmentOriginTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/GraphicsContext2DTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/HitTestDomainAgreementTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ImageSourceRenderNodeTest.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectItemTypedSuffixExecutionTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/MovingOpaqueSourceBoundsTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCacheScaleTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCapturingExecutionCallbackTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCapturingMetadataCallbackTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/BackdropOrderingTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/MaterializedInputCompositeTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/ProductionResourceLifetimeTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RawScopeNestingAndCaptureOffsetTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RegionAnalysisReuseTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RegionAnalyzerTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RendererWideRecordingTests.cs` | 7 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/SymbolicOwningDomainTests.cs` | 3 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/SymbolicSupplyMappingTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/TargetScopeLoweringTests.cs` | 9 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/DeclaredResourceOrderTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/NodeRecordingTransactionTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RawSessionSlotResourceTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingBufferPoolingTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingGateFingerprintTests.cs` | 5 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingIdentityCollisionTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingPerVisitAllocationTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingSideEffectTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RenderNodeRecordingCacheTests.cs` | 7 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RenderRecordingCrossCheckTests.cs` | 9 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/ValueReplaySafetyTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RectClipRenderNodeTest.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeHasChangesTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererAllocationFailureTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererDeviceBoundsTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererExceptionSafetyTests.cs` | 2 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererSnapshotFastPathTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RendererExceptionSafetyTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ResolutionScaleTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/SlotBackedHitTestTests.cs` | 5 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceEffectiveScaleFlowTests.cs` | 13 |
| `tests/Beutl.UnitTests/NodeGraph/ConfigureNodeOwnershipTests.cs` | 2 |
| `tests/Beutl.UnitTests/NodeGraph/NodeGraphFilterEffectRenderNodeTests.cs` | 6 |
| `tests/Beutl.UnitTests/ProjectSystem/SceneDrawableScaleTests.cs` | 1 |

## Historical tests at 83e63689d

| File | Declared Process overrides |
| --- | ---: |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCacheScaleTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeProcessorExceptionSafetyTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RendererExceptionSafetyTests.cs` | 1 |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceEffectiveScaleFlowTests.cs` | 3 |
| `tests/Beutl.UnitTests/NodeGraph/NodeGraphFilterEffectRenderNodeTests.cs` | 1 |

## Historical production at 83e63689d

The original 29-override baseline was derived from the captured production table:
exclude `CompleteTargetRenderNode`, `DrawableGroup`, and `FilterEffectInputRenderNode`,
then include two overrides in `DrawableGroup` and one in the former
`OperationWrapperRenderNode`.
