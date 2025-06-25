using System;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Nodes;

namespace Beutl.UnitTests.Engine.Audio.Graph;

[TestFixture]
public class AudioGraphBuilderTests
{
    private class TestNode : AudioNode
    {
        public string Name { get; set; } = string.Empty;
        
        public override AudioBuffer Process(AudioProcessContext context)
        {
            return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
        }
    }

    [Test]
    public void AddNode_ValidNode_AddsSuccessfully()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var node = new TestNode { Name = "Test" };
        
        // Act
        var result = builder.AddNode(node);
        
        // Assert
        Assert.That(result, Is.SameAs(node));
    }

    [Test]
    public void AddNode_NullNode_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddNode<TestNode>(null!));
    }

    [Test]
    public void AddNode_SameNodeTwice_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var node = new TestNode { Name = "Test" };
        builder.AddNode(node);
        
        // Act & Assert
        Assert.Throws<AudioGraphBuildException>(() => builder.AddNode(node));
    }

    [Test]
    public void Connect_ValidNodes_ConnectsSuccessfully()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var node1 = builder.AddNode(new TestNode { Name = "Node1" });
        var node2 = builder.AddNode(new TestNode { Name = "Node2" });
        
        // Act
        builder.Connect(node1, node2);
        
        // Assert
        Assert.That(node2.Inputs, Contains.Item(node1));
    }

    [Test]
    public void Connect_NullNodes_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var node = builder.AddNode(new TestNode());
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.Connect(null!, node));
        Assert.Throws<ArgumentNullException>(() => builder.Connect(node, null!));
    }

    [Test]
    public void Connect_NodeNotInBuilder_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var node1 = new TestNode { Name = "Node1" };
        var node2 = builder.AddNode(new TestNode { Name = "Node2" });
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Connect(node1, node2));
        Assert.Throws<InvalidOperationException>(() => builder.Connect(node2, node1));
    }

    [Test]
    public void Connect_CreatesCycle_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var node1 = builder.AddNode(new TestNode { Name = "Node1" });
        var node2 = builder.AddNode(new TestNode { Name = "Node2" });
        var node3 = builder.AddNode(new TestNode { Name = "Node3" });
        
        builder.Connect(node1, node2);
        builder.Connect(node2, node3);
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Connect(node3, node1));
    }

    [Test]
    public void SetOutput_ValidNode_SetsSuccessfully()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var outputNode = builder.AddNode(new TestNode { Name = "Output" });
        
        // Act & Assert
        Assert.DoesNotThrow(() => builder.SetOutput(outputNode));
    }

    [Test]
    public void SetOutput_NullNode_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.SetOutput(null!));
    }

    [Test]
    public void SetOutput_NodeNotInBuilder_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var outputNode = new TestNode { Name = "Output" };
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.SetOutput(outputNode));
    }

    [Test]
    public void Build_ValidGraph_BuildsSuccessfully()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var sourceNode = builder.AddNode(new TestNode { Name = "Source" });
        var gainNode = builder.AddNode(new TestNode { Name = "Gain" });
        var outputNode = builder.AddNode(new TestNode { Name = "Output" });
        
        builder.Connect(sourceNode, gainNode);
        builder.Connect(gainNode, outputNode);
        builder.SetOutput(outputNode);
        
        // Act
        using var graph = builder.Build();
        
        // Assert
        Assert.That(graph.OutputNode, Is.SameAs(outputNode));
        Assert.That(graph.Nodes.Count, Is.EqualTo(3));
    }

    [Test]
    public void Build_NoOutputSet_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        builder.AddNode(new TestNode { Name = "Node" });
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void Build_TwiceOnSameBuilder_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var outputNode = builder.AddNode(new TestNode { Name = "Output" });
        builder.SetOutput(outputNode);
        
        using var graph1 = builder.Build();
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void AddNode_AfterBuild_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var outputNode = builder.AddNode(new TestNode { Name = "Output" });
        builder.SetOutput(outputNode);
        
        using var graph = builder.Build();
        
        // Act & Assert
        Assert.Throws<AudioGraphBuildException>(() => builder.AddNode(new TestNode()));
    }

    [Test]
    public void Connect_AfterBuild_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var node1 = builder.AddNode(new TestNode { Name = "Node1" });
        var node2 = builder.AddNode(new TestNode { Name = "Node2" });
        builder.SetOutput(node1);
        
        using var graph = builder.Build();
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Connect(node1, node2));
    }

    [Test]
    public void SetOutput_AfterBuild_ThrowsException()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var outputNode = builder.AddNode(new TestNode { Name = "Output" });
        builder.SetOutput(outputNode);
        
        using var graph = builder.Build();
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.SetOutput(outputNode));
    }

    [Test]
    public void Build_ComplexGraph_PreservesTopologicalOrder()
    {
        // Arrange
        var builder = new AudioGraphBuilder();
        var source1 = builder.AddNode(new TestNode { Name = "Source1" });
        var source2 = builder.AddNode(new TestNode { Name = "Source2" });
        var mixer = builder.AddNode(new TestNode { Name = "Mixer" });
        var effect = builder.AddNode(new TestNode { Name = "Effect" });
        var output = builder.AddNode(new TestNode { Name = "Output" });
        
        builder.Connect(source1, mixer);
        builder.Connect(source2, mixer);
        builder.Connect(mixer, effect);
        builder.Connect(effect, output);
        builder.SetOutput(output);
        
        // Act
        using var graph = builder.Build();
        
        // Assert
        var nodes = graph.Nodes;
        var outputIndex = nodes.IndexOf(output);
        var effectIndex = nodes.IndexOf(effect);
        var mixerIndex = nodes.IndexOf(mixer);
        var source1Index = nodes.IndexOf(source1);
        var source2Index = nodes.IndexOf(source2);
        
        // Sources should come before mixer
        Assert.That(source1Index, Is.LessThan(mixerIndex));
        Assert.That(source2Index, Is.LessThan(mixerIndex));
        
        // Mixer should come before effect
        Assert.That(mixerIndex, Is.LessThan(effectIndex));
        
        // Effect should come before output
        Assert.That(effectIndex, Is.LessThan(outputIndex));
    }
}