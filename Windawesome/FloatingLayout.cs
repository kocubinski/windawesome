
namespace Windawesome
{
	public sealed class FloatingLayout : ILayout
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

		void ILayout.Dispose()
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
