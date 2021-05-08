using System.Linq;

using BEditor.Data;
using BEditor.Media;

using Microsoft.Extensions.CommandLineUtils;

namespace BEditor
{
    public static class Tool
    {
        public static int? TryParse(this CommandArgument arg)
        {
            if (int.TryParse(arg.Value, out var f))
            {
                return f;
            }
            else
            {
                return null;
            }
        }
        public static bool Clamp(this Scene self, ClipElement? clip_, ref Frame start, ref Frame end, int layer)
        {
            var array = self.GetLayer(layer).ToArray();

            for (int i = 0; i < array.Length; i++)
            {
                ClipElement? clip = array[i];

                if (clip != clip_)
                {
                    if (clip.InRange(start, end, out var type))
                    {
                        if (type == RangeType.StartEnd)
                        {
                            return false;
                        }
                        else if (type == RangeType.Start)
                        {
                            start = clip.End;

                            return true;
                        }
                        else if (type == RangeType.End)
                        {
                            end = clip.Start;

                            return true;
                        }


                        return false;
                    }
                }
            }

            return true;
        }
        public static bool InRange(this Scene self, Frame start, Frame end, int layer)
        {
            foreach (var clip in self.GetLayer(layer))
            {
                if (clip.InRange(start, end))
                {
                    return false;
                }
            }

            return true;
        }
        public static bool InRange(this Scene self, ClipElement clip_, Frame start, Frame end, int layer)
        {
            var array = self.GetLayer(layer).ToArray();

            for (int i = 0; i < array.Length; i++)
            {
                ClipElement? clip = array[i];

                if (clip != clip_)
                {
                    if (clip.InRange(start, end, out _))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        // このクリップと被る場合はtrue
        public static bool InRange(this ClipElement self, Frame start, Frame end)
        {
            if (self.Start <= start && end <= self.End)
            {
                return true;
            }
            else if (self.Start <= start && start < self.End)
            {
                return true;
            }
            else if (self.Start < end && end <= self.End)
            {
                return true;
            }
            else if (start <= self.Start && self.End <= end)
            {
                return true;
            }

            return false;
        }
        public static bool InRange(this ClipElement self, Frame start, Frame end, out RangeType type)
        {
            if (self.Start <= start && end <= self.End)
            {
                type = RangeType.StartEnd;

                return true;
            }
            else if (self.Start <= start && start < self.End)
            {
                type = RangeType.Start;

                return true;
            }
            else if (self.Start < end && end <= self.End)
            {
                type = RangeType.End;

                return true;
            }
            else if (start <= self.Start && self.End <= end)
            {
                type = RangeType.StartEnd;

                return true;
            }
            type = default;
            return false;
        }

        public enum RangeType
        {
            StartEnd,
            Start,
            End
        }
    }
}