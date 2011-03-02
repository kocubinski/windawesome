using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public class Bar
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

		public void OnWidgetControlsChanged(IWidget widget, IEnumerable<Control> controlsRemoved, IEnumerable<Control> controlsAdded)
		{
			WidgetControlsChanged(widget, controlsRemoved, controlsAdded);
		}

		public void OnSpanWidgetControlsAdded(IWidget widget, IEnumerable<Control> controls)
		{
			SpanWidgetControlsAdded(widget, controls);
		}

		public void OnSpanWidgetControlsRemoved(IWidget widget, IEnumerable<Control> controls)
		{
			SpanWidgetControlsRemoved(widget, controls);
		}

		public void OnFixedWidthWidgetWidthChanged(IWidget widget)
		{
			FixedWidthWidgetWidthChanged(widget);
		}

		#endregion

		private Form CreateForm()
		{
			Form form = new Form();

			form.Name = "Windawesome Bar";
			form.StartPosition = FormStartPosition.Manual;
			form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
			form.AutoValidate = AutoValidate.Disable;
			form.CausesValidation = false;
			form.ControlBox = false;
			form.MaximizeBox = false;
			form.MinimizeBox = false;
			form.ShowIcon = false;
			form.ShowInTaskbar = false;
			form.SizeGripStyle = SizeGripStyle.Hide;
			form.AutoScaleMode = AutoScaleMode.Font;
			form.AutoScroll = false;
			form.AutoSize = false;
			form.HelpButton = false;
			form.TopLevel = true;
			form.WindowState = FormWindowState.Normal;
			form.TopMost = true;
			form.VisibleChanged += form_VisibleChanged;
			form.FormClosing += (s, ea) => ea.Cancel = true;
			form.MinimumSize = new Size(0, barHeight);
			form.Height = barHeight;

			// make the bar not activatable
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(form.Handle);
			NativeMethods.SetWindowExStyleLongPtr(form.Handle, exStyle | NativeMethods.WS_EX.WS_EX_NOACTIVATE);

			return form;
		}

		public override int GetHashCode()
		{
			return form.Handle.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			Bar bar = obj as Bar;
			return bar != null && form.Handle == bar.form.Handle;
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

		public Bar(IList<IWidget> containsLeftAligned, IList<IWidget> containsRightAligned,
			IList<IWidget> middleAlignedWidgets, int barHeight = 20, Font font = null, Color? backgroundColor = null)
		{
			this.leftAlignedWidgets = containsLeftAligned.ToArray();
			this.rightAlignedWidgets = containsRightAligned.ToArray();
			this.middleAlignedWidgets = middleAlignedWidgets.ToArray();
			this.barHeight = barHeight;
			this.font = font ?? new Font("Lucida Console", 8);

			form = CreateForm();
			if (backgroundColor != null)
			{
				form.BackColor = backgroundColor.Value;
			}

			// make the bar visible even when the user uses "Show Desktop" or ALT-TABs to the desktop
			NativeMethods.SetParent(form.Handle, desktopWindowHandle);
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

			WidgetControlsChanged = Bar_WidgetControlsChanged;
			SpanWidgetControlsAdded = Bar_SpanWidgetControlsAdded;
			SpanWidgetControlsRemoved = Bar_SpanWidgetControlsRemoved;
			FixedWidthWidgetWidthChanged = Bar_FixedWidthWidgetWidthChanged;

			leftAlignedWidgets.ForEach(w => w.InitializeWidget(this));
			rightAlignedWidgets.ForEach(w => w.InitializeWidget(this));
			middleAlignedWidgets.ForEach(w => w.InitializeWidget(this));

			PlaceControls();
		}

		internal void Dispose()
		{
			// statically dispose of any widgets not already dispsed
			leftAlignedWidgets.Concat(rightAlignedWidgets).Concat(middleAlignedWidgets).
				Where(w => widgetTypes.Contains(w.GetType())).
				ForEach(w => { w.StaticDispose(); widgetTypes.Remove(w.GetType()); });

			leftAlignedWidgets.ForEach(w => w.Dispose());
			rightAlignedWidgets.ForEach(w => w.Dispose());
			middleAlignedWidgets.ForEach(w => w.Dispose());

			form.Dispose();
		}

		#endregion

		#region Event Handlers

		private void Bar_WidgetControlsChanged(IWidget widget, IEnumerable<Control> controlsRemoved, IEnumerable<Control> controlsAdded)
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

		private void Bar_SpanWidgetControlsAdded(IWidget widget, IEnumerable<Control> controls)
		{
			this.form.SuspendLayout();

			controls.ForEach(this.form.Controls.Add);

			this.form.ResumeLayout();
		}

		private void Bar_SpanWidgetControlsRemoved(IWidget widget, IEnumerable<Control> controls)
		{
			this.form.SuspendLayout();

			controls.ForEach(this.form.Controls.Remove);

			this.form.ResumeLayout();
		}

		private void Bar_FixedWidthWidgetWidthChanged(IWidget widget)
		{
			this.form.SuspendLayout();

			ResizeWidgets(widget);

			this.form.ResumeLayout();
		}

		private void form_VisibleChanged(object sender, EventArgs e)
		{
			if (form.Visible)
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
			for (int i = fromIndex; i < leftAlignedWidgets.Length; i++)
			{
				var w = leftAlignedWidgets[i];
				w.RepositionControls(fromX, -1);
				fromX = w.GetRight();
			}

			rightmostLeftAlign = fromX;
		}

		private void RepositionRightAlignedWidgets(int fromIndex, int toX)
		{
			for (int i = fromIndex; i >= 0; i--)
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
				int x = rightmostLeftAlign;
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

			leftmost = SystemInformation.WorkingArea.Left;
			rightmost = SystemInformation.WorkingArea.Right;
			int x = leftmost;
			foreach (var widget in leftAlignedWidgets)
			{
				var controls = widget.GetControls(x, -1);
				controls.ForEach(this.form.Controls.Add);
				x = controls.FirstOrDefault() != null ? controls.Max(c => c.Right) : x;
			}
			rightmostLeftAlign = x;

			x = rightmost;
			foreach (var widget in rightAlignedWidgets.Reverse())
			{
				var controls = widget.GetControls(-1, x);
				controls.ForEach(this.form.Controls.Add);
				x = controls.FirstOrDefault() != null ? controls.Min(c => c.Left) : x;
			}
			leftmostRightAlign = x;

			if (middleAlignedWidgets.Length > 0)
			{
				var eachWidth = (leftmostRightAlign - rightmostLeftAlign) / middleAlignedWidgets.Length;
				x = rightmostLeftAlign;
				foreach (var widget in middleAlignedWidgets)
				{
					var controls = widget.GetControls(x, x + eachWidth);
					controls.ForEach(this.form.Controls.Add);
					x += eachWidth;
				}
			}

			this.form.ResumeLayout();
		}

		public Label CreateLabel(string text, int xLocation, int width = -1)
		{
			Label label = new Label();
			label.SuspendLayout();
			label.AutoSize = false;
			label.AutoEllipsis = true;
			label.Text = text;
			label.Font = font;
			label.Size = new Size(width == -1 ? TextRenderer.MeasureText(label.Text, label.Font).Width : width, barHeight);
			label.Location = new Point(xLocation, 0);
			label.TextAlign = ContentAlignment.MiddleLeft; // TODO: this doesn't work when there are ellipsis
			label.ResumeLayout();

			return label;
		}
	}
}
