using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Media;
using BEditor.Primitive;

using BenchmarkDotNet.Attributes;

namespace Benchmark
{
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    public class SceneGetFrameBench
    {
        private static readonly int frame = 250;

        public SceneGetFrameBench()
        {
            Scene = new(1920, 1080);
            for (int i = 0; i < 100; i++)
            {
                Scene.Add(new(100, 200, i, Scene, PrimitiveTypes.ShapeMetadata));
            }
        }

        public Scene Scene { get; }

        [Benchmark]
        public void Enumerable()
        {
            foreach (var item in Scene.Datas
                .AsParallel()
                .Where(item => item.Start <= frame && frame < item.End)
                .Where(item => !Scene.HideLayer.Exists(x => x == item.Layer))
                .OrderBy(item => item.Layer))
            {
                item.Clone();
            }
        }

        [Benchmark]
        public void Array()
        {
            var array = Scene.Datas
                .AsParallel()
                .Where(item => item.Start <= frame && frame < item.End)
                .Where(item => !Scene.HideLayer.Exists(x => x == item.Layer))
                .ToArray();

            System.Array.Sort(array, (x, y) => x.Layer - y.Layer);

            for (int i = 0; i < array.Length; i++)
            {
                array[i].Clone();
            }
        }
    }
}