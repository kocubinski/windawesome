using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public class CpuMonitorWidget : IWidget
	{
		private readonly PerformanceCounter counter;
		private Label label;
		private readonly Timer updateTimer;
		private int left, right;
		private bool isLeft;
		private readonly string prefix;
		private readonly string postfix;

		public CpuMonitorWidget(string prefix = "CPU:", string postfix = "%", int updateTime = 1000)
		{
			updateTimer = new Timer { Interval = updateTime };
			updateTimer.Tick += OnTimerTick;

			this.prefix = prefix;
			this.postfix = postfix;

			counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
		}

		private void OnTimerTick(object sender, System.EventArgs e)
		{
			label.Text = prefix + counter.NextValue().ToString("00") + postfix;
		}

		#region IWidget Members

		public WidgetType GetWidgetType()
		{
			return WidgetType.FixedWidth;
		}

		public void StaticInitializeWidget(Windawesome windawesome, Config config)
		{
		}

		public void InitializeWidget(Bar bar)
		{
			label = bar.CreateLabel(prefix + counter.NextValue().ToString("00") + postfix, 0);
			label.TextAlign = ContentAlignment.MiddleCenter;
		}

		public IEnumerable<Control> GetControls(int left, int right)
		{
			isLeft = right == -1;

			RepositionControls(left, right);

			return new[] { label };
		}

		public void RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			if (isLeft)
			{
				label.Location = new Point(left, 0);
				this.right = label.Right;
			}
			else
			{
				label.Location = new Point(right - label.Width, 0);
				this.left = label.Left;
			}
		}

		public int GetLeft()
		{
			return left;
		}

		public int GetRight()
		{
			return right;
		}

		public void WidgetShown()
		{
			updateTimer.Start();
		}

		public void WidgetHidden()
		{
			updateTimer.Stop();
		}

		public void StaticDispose()
		{
		}

		public void Dispose()
		{
		}

		#endregion
	}
}
