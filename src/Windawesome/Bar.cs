using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class Bar
	{
		private static readonly HashSet<Type> widgetTypes;
		private static readonly IntPtr desktopWindowHandle;

		public readonly int barHeight;
		public readonly Form form;
		private readonly IWidget[] leftAlignedWidgets;
		private readonly IWidget[] rightAlignedWidgets;
		private readonly IWidget[] middleAlignedWidgets;
		private readonly Font font;

		private int rightmostLeftAlign;
		private int leftmostRightAlign;

		internal int leftmost { get; private set; }
		internal int rightmost { get; private set; }

		#region Events

		private delegate void SpanWidgetControlsAddedEventHandler(IWidget widget, IEnumerable<Control> controls);
		private event SpanWidgetControlsAddedEventHandler SpanWidgetControlsAdded;

		private delegate void SpanWidgetControlsRemovedEventHandler(IWidget widget, IEnumerable<Control> controls);
		private event SpanWidgetControlsRemovedEventHandler SpanWidgetControlsRemoved;

		private delegate void FixedWidthWidgetWidthChangedEventHandler(IWidget widget);
		private event FixedWidthWidgetWidthChangedEventHandler FixedWidthWidgetWidthChanged;

		private delegate void WidgetControlsChangedEventHandler(IWidget widget, IEnumerable<Control> oldControls, IEnumerable<Control> newControls);
		private event WidgetControlsChangedEventHandler WidgetControlsChanged;

		public void DoWidgetControlsChanged(IWidget widget, IEnumerable<Control> controlsRemoved, IEnumerable<Control> controlsAdded)
		{
			WidgetControlsChanged(widget, controlsRemoved, controlsAdded);
		}

		public void DoSpanWidgetControlsAdded(IWidget widget, IEnumerable<Control> controls)
		{
			SpanWidgetControlsAdded(widget, controls);
		}

		public void DoSpanWidgetControlsRemoved(IWidget widget, IEnumerable<Control> controls)
		{
			SpanWidgetControlsRemoved(widget, controls);
		}

		public void DoFixedWidthWidgetWidthChanged(IWidget widget)
		{
			FixedWidthWidgetWidthChanged(widget);
		}

		#endregion

		private Form CreateForm()
		{
			var newForm = new Form
			    {
			        StartPosition = FormStartPosition.Manual,
			        FormBorderStyle = FormBorderStyle.FixedToolWindow,
			        AutoValidate = AutoValidate.Disable,
			        CausesValidation = false,
			        ControlBox = false,
			        MaximizeBox = false,
			        MinimizeBox = false,
			        ShowIcon = false,
			        ShowInTaskbar = false,
			        SizeGripStyle = SizeGripStyle.Hide,
			        AutoScaleMode = AutoScaleMode.Font,
			        AutoScroll = false,
			        AutoSize = false,
			        HelpButton = false,
			        TopLevel = true,
			        WindowState = FormWindowState.Normal,
			        TopMost = true,
					MinimumSize = new Size(0, this.barHeight),
			        Height = this.barHeight
			    };

			newForm.VisibleChanged += this.OnFormVisibleChanged;
			newForm.FormClosing += (s, ea) => ea.Cancel = true;

			// make the bar not activatable
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(newForm.Handle);
			NativeMethods.SetWindowExStyleLongPtr(newForm.Handle, exStyle | NativeMethods.WS_EX.WS_EX_NOACTIVATE);

			return newForm;
		}

		public override int GetHashCode()
		{
			return this.form.Handle.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			var bar = obj as Bar;
			return bar != null && this.form.Handle == bar.form.Handle;
		}

		#region Construction and Destruction

		static Bar()
		{
			widgetTypes = new HashSet<Type>();

			desktopWindowHandle = NativeMethods.FindWindow("Progman", "Program Manager");
			if (Windawesome.isAtLeast7)
			{
				desktopWindowHandle = NativeMethods.FindWindowEx(desktopWindowHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
				desktopWindowHandle = NativeMethods.FindWindowEx(desktopWindowHandle, IntPtr.Zero, "SysListView32", "FolderView");
			}
		}

		public Bar(IEnumerable<IWidget> containsLeftAligned, IEnumerable<IWidget> containsRightAligned,
			IEnumerable<IWidget> middleAlignedWidgets, int barHeight = 20, Font font = null, Color? backgroundColor = null)
		{
			leftAlignedWidgets = containsLeftAligned.ToArray();
			rightAlignedWidgets = containsRightAligned.ToArray();
			this.middleAlignedWidgets = middleAlignedWidgets.ToArray();
			this.barHeight = barHeight;
			this.font = font ?? new Font("Lucida Console", 8);

			this.form = CreateForm();
			if (backgroundColor != null)
			{
				this.form.BackColor = backgroundColor.Value;
			}

			// make the bar visible even when the user uses "Show Desktop" or ALT-TABs to the desktop
			NativeMethods.SetParent(this.form.Handle, desktopWindowHandle);
		}

		internal void InitializeBar(Windawesome windawesome, Config config)
		{
			if (leftAlignedWidgets.Any(w => w.GetWidgetType() == WidgetType.Span) ||
				rightAlignedWidgets.Any(w => w.GetWidgetType() == WidgetType.Span) ||
				middleAlignedWidgets.Any(w => w.GetWidgetType() != WidgetType.Span))
			{
				throw new Exception("Left/Right aligned widgets cannot be of type \"Span\" and\nmiddle aligned widgets cannot be anything other than \"Span\"!");
			}

			// statically initialize any widgets not already initialized
			leftAlignedWidgets.Concat(rightAlignedWidgets).Concat(middleAlignedWidgets).
				Where(w => !widgetTypes.Contains(w.GetType())).
				ForEach(w => { w.StaticInitializeWidget(windawesome, config); widgetTypes.Add(w.GetType()); });

			WidgetControlsChanged = OnWidgetControlsChanged;
			SpanWidgetControlsAdded = OnSpanWidgetControlsAdded;
			SpanWidgetControlsRemoved = OnSpanWidgetControlsRemoved;
			FixedWidthWidgetWidthChanged = OnFixedWidthWidgetWidthChanged;

			leftAlignedWidgets.ForEach(w => w.InitializeWidget(this));
			rightAlignedWidgets.ForEach(w => w.InitializeWidget(this));
			middleAlignedWidgets.ForEach(w => w.InitializeWidget(this));

			PlaceControls();
		}

		internal void Dispose()
		{
			leftAlignedWidgets.ForEach(w => w.Dispose());
			rightAlignedWidgets.ForEach(w => w.Dispose());
			middleAlignedWidgets.ForEach(w => w.Dispose());

			// statically dispose of any widgets not already dispsed
			leftAlignedWidgets.Concat(rightAlignedWidgets).Concat(middleAlignedWidgets).
				Where(w => widgetTypes.Contains(w.GetType())).
				ForEach(w => { w.StaticDispose(); widgetTypes.Remove(w.GetType()); });

			this.form.Dispose();
		}

		#endregion

		#region Event Handlers

		private void OnWidgetControlsChanged(IWidget widget, IEnumerable<Control> controlsRemoved, IEnumerable<Control> controlsAdded)
		{
			this.form.SuspendLayout();

			controlsRemoved.ForEach(this.form.Controls.Remove);
			controlsAdded.ForEach(this.form.Controls.Add);

			if (widget.GetWidgetType() == WidgetType.FixedWidth)
			{
				ResizeWidgets(widget);
			}

			this.form.ResumeLayout();
		}

		private void OnSpanWidgetControlsAdded(IWidget widget, IEnumerable<Control> controls)
		{
			this.form.SuspendLayout();

			controls.ForEach(this.form.Controls.Add);

			this.form.ResumeLayout();
		}

		private void OnSpanWidgetControlsRemoved(IWidget widget, IEnumerable<Control> controls)
		{
			this.form.SuspendLayout();

			controls.ForEach(this.form.Controls.Remove);

			this.form.ResumeLayout();
		}

		private void OnFixedWidthWidgetWidthChanged(IWidget widget)
		{
			this.form.SuspendLayout();

			ResizeWidgets(widget);

			this.form.ResumeLayout();
		}

		private void OnFormVisibleChanged(object sender, EventArgs e)
		{
			if (this.form.Visible)
			{
				leftAlignedWidgets.ForEach(w => w.WidgetShown());
				rightAlignedWidgets.ForEach(w => w.WidgetShown());
				middleAlignedWidgets.ForEach(w => w.WidgetShown());
			}
			else
			{
				leftAlignedWidgets.ForEach(w => w.WidgetHidden());
				rightAlignedWidgets.ForEach(w => w.WidgetHidden());
				middleAlignedWidgets.ForEach(w => w.WidgetHidden());
			}
		}

		#endregion

		internal void ResizeWidgets(int leftmost, int rightmost)
		{
			this.leftmost = leftmost;
			this.rightmost = rightmost;

			RepositionLeftAlignedWidgets(0, leftmost);
			RepositionRightAlignedWidgets(rightAlignedWidgets.Length - 1, rightmost);
			RepositionMiddleAlignedWidgets();
		}

		private void ResizeWidgets(IWidget widget)
		{
			int index;
			if ((index = Array.IndexOf(leftAlignedWidgets, widget)) != -1)
			{
				RepositionLeftAlignedWidgets(index + 1, widget.GetRight());
			}
			else
			{
				RepositionRightAlignedWidgets(Array.IndexOf(rightAlignedWidgets, widget) - 1, widget.GetLeft());
			}

			RepositionMiddleAlignedWidgets();
		}

		private void RepositionLeftAlignedWidgets(int fromIndex, int fromX)
		{
			for (var i = fromIndex; i < leftAlignedWidgets.Length; i++)
			{
				var w = leftAlignedWidgets[i];
				w.RepositionControls(fromX, -1);
				fromX = w.GetRight();
			}

			rightmostLeftAlign = fromX;
		}

		private void RepositionRightAlignedWidgets(int fromIndex, int toX)
		{
			for (var i = fromIndex; i >= 0; i--)
			{
				var w = rightAlignedWidgets[i];
				w.RepositionControls(-1, toX);
				toX = w.GetLeft();
			}

			leftmostRightAlign = toX;
		}

		private void RepositionMiddleAlignedWidgets()
		{
			if (middleAlignedWidgets.Length > 0)
			{
				var eachWidth = (leftmostRightAlign - rightmostLeftAlign) / middleAlignedWidgets.Length;
				var x = rightmostLeftAlign;
				foreach (var w in middleAlignedWidgets)
				{
					w.RepositionControls(x, x + eachWidth);
					x += eachWidth;
				}
			}
		}

		private void PlaceControls()
		{
			this.form.SuspendLayout();

			this.leftmost = SystemInformation.WorkingArea.Left;
			this.rightmost = SystemInformation.WorkingArea.Right;
			var x = this.leftmost;
			foreach (var controls in this.leftAlignedWidgets.Select(widget => widget.GetControls(x, -1)))
			{
				controls.ForEach(this.form.Controls.Add);
				x = controls.FirstOrDefault() != null ? controls.Max(c => c.Right) : x;
			}
			rightmostLeftAlign = x;

			x = this.rightmost;
			foreach (var controls in this.rightAlignedWidgets.Reverse().Select(widget => widget.GetControls(-1, x)))
			{
				controls.ForEach(this.form.Controls.Add);
				x = controls.FirstOrDefault() != null ? controls.Min(c => c.Left) : x;
			}
			leftmostRightAlign = x;

			if (middleAlignedWidgets.Length > 0)
			{
				var eachWidth = (leftmostRightAlign - rightmostLeftAlign) / middleAlignedWidgets.Length;
				x = rightmostLeftAlign;
				foreach (var controls in this.middleAlignedWidgets.Select(widget => widget.GetControls(x, x + eachWidth)))
				{
					controls.ForEach(this.form.Controls.Add);
					x += eachWidth;
				}
			}

			this.form.ResumeLayout();
		}

		public Label CreateLabel(string text, int xLocation, int width = -1)
		{
			var label = new Label();
			label.SuspendLayout();
			label.AutoSize = false;
			label.AutoEllipsis = true;
			label.Text = text;
			label.Font = font;
			label.Size = new Size(width == -1 ? TextRenderer.MeasureText(label.Text, label.Font).Width : width, this.barHeight);
			label.Location = new Point(xLocation, 0);
			label.TextAlign = ContentAlignment.MiddleLeft; // TODO: this doesn't work when there are ellipsis
			label.ResumeLayout();

			return label;
		}
	}
}
