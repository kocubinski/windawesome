using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public class CpuMonitorWidget : IFixedWidthWidget
	{
		private Bar bar;

		private Label label;
		private bool isLeft;
		private readonly PerformanceCounter counter;
		private readonly Timer updateTimer;
		private readonly string prefix;
		private readonly string postfix;
		private readonly Color backgroundColor;

		public CpuMonitorWidget(string prefix = "CPU:", string postfix = "%", int updateTime = 1000, Color? backgroundColor = null)
		{
			updateTimer = new Timer { Interval = updateTime };
			updateTimer.Tick += OnTimerTick;

			this.prefix = prefix;
			this.postfix = postfix;

			this.backgroundColor = backgroundColor ?? Color.White;

			counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
		}

		private void OnTimerTick(object sender, System.EventArgs e)
		{
			var oldLeft = label.Left;
			var oldRight = label.Right;
			var nextValue = counter.NextValue();

			label.Text = prefix + nextValue.ToString("00") + postfix;

			if (nextValue == 100)
			{
				this.RepositionControls(oldLeft, oldRight);
				bar.DoFixedWidthWidgetWidthChanged(this);
			}
		}

		#region IWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome)
		{
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			this.bar = bar;

			label = bar.CreateLabel(prefix + counter.NextValue().ToString("00") + postfix, 0);
			label.BackColor = backgroundColor;
			label.TextAlign = ContentAlignment.MiddleCenter;

			bar.BarShown += () => updateTimer.Start();
			bar.BarHidden += () => updateTimer.Stop();
		}

		IEnumerable<Control> IFixedWidthWidget.GetInitialControls(bool isLeft)
		{
			this.isLeft = isLeft;

			return new[] { label };
		}

		public void RepositionControls(int left, int right)
		{
			if (isLeft)
			{
				label.Location = new Point(left, 0);
			}
			else
			{
				label.Location = new Point(right - label.Width, 0);
			}
		}

		int IWidget.GetLeft()
		{
			return label.Left;
		}

		int IWidget.GetRight()
		{
			return label.Right;
		}

		void IWidget.StaticDispose()
		{
		}

		void IWidget.Dispose()
		{
		}

		void IWidget.Refresh()
		{
		}

		#endregion
	}
}
