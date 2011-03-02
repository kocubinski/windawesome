
namespace Windawesome
{
	public interface IPlugin
	{
		void InitializePlugin(Windawesome windawesome, Config config);

		void Dispose();
	}
}
