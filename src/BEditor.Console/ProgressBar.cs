using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BEditor
{
    public class ProgressBar : IDisposable, IProgress<double>
    {
        private const int BLOCK_COUNT = 20;
        private const string ANIMATION = @"|/-\";
        private readonly TimeSpan _animationInterval = TimeSpan.FromSeconds(1.0 / 8);

        private readonly Timer _timer;

        private double _currentProgress = 0;
        private string _currentText = string.Empty;
        private bool _isDisposed = false;
        private int _animationIndex = 0;

        public ProgressBar()
        {
            _timer = new Timer(TimerHandler);

            if (!Console.IsOutputRedirected)
            {
                ResetTimer();
            }
        }

        public void Report(double value)
        {
            // Make sure value is in [0..1] range
            value = Math.Max(0, Math.Min(1, value));
            Interlocked.Exchange(ref _currentProgress, value);
        }

        private void TimerHandler(object? state)
        {
            lock (_timer)
            {
                if (_isDisposed) return;

                int progressBlockCount = (int)(_currentProgress * BLOCK_COUNT);
                int percent = (int)(_currentProgress * 100);
                string text = string.Format("[{0}{1}] {2,3}% {3}",
                    new string('#', progressBlockCount), new string('-', BLOCK_COUNT - progressBlockCount),
                    percent,
                    ANIMATION[_animationIndex++ % ANIMATION.Length]);
                UpdateText(text);

                ResetTimer();
            }
        }

        private void UpdateText(string text)
        {
            // Get length of common portion
            int commonPrefixLength = 0;
            int commonLength = Math.Min(_currentText.Length, text.Length);
            while (commonPrefixLength < commonLength && text[commonPrefixLength] == _currentText[commonPrefixLength])
            {
                commonPrefixLength++;
            }

            // Backtrack to the first differing character
            var outputBuilder = new StringBuilder();
            outputBuilder.Append('\b', _currentText.Length - commonPrefixLength);

            // Output new suffix
            outputBuilder.Append(text[commonPrefixLength..]);

            // If the new text is shorter than the old one: delete overlapping characters
            int overlapCount = _currentText.Length - text.Length;
            if (overlapCount > 0)
            {
                outputBuilder.Append(' ', overlapCount);
                outputBuilder.Append('\b', overlapCount);
            }

            Console.Write(outputBuilder);
            _currentText = text;
        }

        private void ResetTimer()
        {
            _timer.Change(_animationInterval, TimeSpan.FromMilliseconds(-1));
        }

        public void Dispose()
        {
            lock (_timer)
            {
                _isDisposed = true;
                UpdateText(string.Empty);

                GC.SuppressFinalize(this);
            }
        }
    }
}