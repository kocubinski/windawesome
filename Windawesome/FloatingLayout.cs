using System.Collections.Generic;
using System.Drawing;

namespace Windawesome
{
	public class FloatingLayout : ILayout
	{
		#region ILayout Members

		string ILayout.LayoutSymbol()
		{
			return "><>";
		}

		public string LayoutName()
		{
			return "Floating";
		}

		void ILayout.Initialize(Workspace workspace)
		{
		}

		bool ILayout.ShouldSaveAndRestoreSharedWindowsPosition()
		{
			return true;
		}

		void ILayout.Reposition()
		{
		}

		void ILayout.WindowMinimized(Window window)
		{
		}

		void ILayout.WindowRestored(Window window)
		{
		}

		void ILayout.WindowCreated(Window window)
		{
		}

		void ILayout.WindowDestroyed(Window window)
		{
		}

		#endregion
	}
}
