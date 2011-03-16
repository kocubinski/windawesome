using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using IronPython.Hosting;
using IronRuby;
using Microsoft.Scripting.Hosting;

namespace Windawesome
{
	public class Config
	{
		internal void LoadPlugins(Windawesome windawesome)
		{
			string layoutsDirName = "Layouts";
			string widgetsDirName = "Widgets";
			string pluginsDirName = "Plugins";
			string configDirName  = "Config";

			if (!Directory.Exists(configDirName) || Directory.EnumerateFiles(configDirName).FirstOrDefault() == null)
			{
				throw new Exception("You HAVE to have a " + configDirName + " directory in the folder and it must " +
					"contain at least one Python or Ruby file that initializes all instance variables in 'config' " +
					"that don't have default values!");
			}
			if (!Directory.Exists(layoutsDirName))
			{
				Directory.CreateDirectory(layoutsDirName);
			}
			if (!Directory.Exists(widgetsDirName))
			{
				Directory.CreateDirectory(widgetsDirName);
			}
			if (!Directory.Exists(pluginsDirName))
			{
				Directory.CreateDirectory(pluginsDirName);
			}
			var files =
				Directory.EnumerateFiles(layoutsDirName).Select(fileName => new FileInfo(fileName)) .Concat(
				Directory.EnumerateFiles(widgetsDirName).Select(fileName => new FileInfo(fileName))).Concat(
				Directory.EnumerateFiles(pluginsDirName).Select(fileName => new FileInfo(fileName))).Concat(
				Directory.EnumerateFiles(configDirName) .Select(fileName => new FileInfo(fileName)));

			PluginLoader.LoadAll(windawesome, this, files);
		}

		public IPlugin[] plugins;
		public Bar[] bars;
		public ILayout[] layouts;
		public int workspacesCount;
		public Workspace[] workspaces;
		public int startingWorkspace = 1;
		public ProgramRule[] programRules;
		public int borderWidth = -1;
		public int paddedBorderWidth = -1;

		private class PluginLoader
		{
			private static ScriptEngine pythonEngine;
			private static ScriptEngine rubyEngine;

			internal PluginLoader()
			{
				pythonEngine = null;
				rubyEngine = null;
			}

			private static ScriptEngine PythonEngine
			{
				get
				{
					if (pythonEngine == null)
					{
						pythonEngine = Python.CreateEngine();
						InitializeScriptEngine(pythonEngine);
					}
					return pythonEngine;
				}
			}

			private static ScriptEngine RubyEngine
			{
				get
				{
					if (rubyEngine == null)
					{
						rubyEngine = Ruby.CreateEngine();
						InitializeScriptEngine(rubyEngine);
					}
					return rubyEngine;
				}
			}

			private static void InitializeScriptEngine(ScriptEngine engine)
			{
				var searchPaths = engine.GetSearchPaths().ToList();
				searchPaths.Add(System.Environment.CurrentDirectory);
				engine.SetSearchPaths(searchPaths);

				AppDomain.CurrentDomain.GetAssemblies().ForEach(engine.Runtime.LoadAssembly);
			}

			private static ScriptEngine GetEngineForFile(FileInfo file)
			{
				switch (file.Extension)
				{
					case ".ir":
					case ".rb":
						return RubyEngine;
					case ".ipy":
					case ".py":
						return PythonEngine;
					case ".dll":
						var assembly = Assembly.LoadFrom(file.FullName);
						if (rubyEngine != null)
						{
							rubyEngine.Runtime.LoadAssembly(assembly);
						}
						if (pythonEngine != null)
						{
							pythonEngine.Runtime.LoadAssembly(assembly);
						}
						break;
				}

				return null;
			}

			internal static void LoadAll(Windawesome windawesome, Config config, IEnumerable<FileInfo> files)
			{
				ScriptScope scope = null;
				ScriptEngine previousLanguage = null;
				foreach (var file in files)
				{
					var engine = GetEngineForFile(file);
					if (engine != null)
					{
						if (scope == null)
						{
							scope = engine.CreateScope();
							scope.SetVariable("windawesome", windawesome);
							scope.SetVariable("config", config);
						}
						else if (previousLanguage != engine)
						{
							var oldScope = scope;
							scope = engine.CreateScope();
							oldScope.GetItems().
								Where(variable => variable.Value != null).
								ForEach(variable =>	scope.SetVariable(variable.Key, variable.Value));
							previousLanguage.Runtime.Globals.GetItems().
								Where(variable => variable.Value != null).
								ForEach(variable => scope.SetVariable(variable.Key, variable.Value));
						}

						scope = engine.ExecuteFile(file.FullName, scope);
						previousLanguage = engine;
					}
				}
			}
		}
	}

	public enum State : int
	{
		SHOWN  = 0,
		HIDDEN = 1,
		AS_IS  = 2
	}

	public class ProgramRule
	{
		internal readonly Regex className;
		internal readonly Regex displayName;
		internal readonly NativeMethods.WS styleContains;
		internal readonly NativeMethods.WS styleNotContains;
		internal readonly NativeMethods.WS_EX styleExContains;
		internal readonly NativeMethods.WS_EX styleExNotContains;
		internal readonly bool isManaged;
		internal readonly int windowCreatedDelay;
		internal readonly bool switchToOnCreated;
		internal readonly Rule[] rules;

		internal bool IsMatch(string cName, string dName, NativeMethods.WS style, NativeMethods.WS_EX exStyle)
		{
			return className.IsMatch(cName) && displayName.IsMatch(dName) &&
				(style & styleContains) == styleContains && (style & styleNotContains) == 0 &&
				(exStyle & styleExContains) == styleExContains && (exStyle & styleExNotContains) == 0;
		}

		public ProgramRule(string className = ".*", string displayName = ".*",
			NativeMethods.WS styleContains = 0, NativeMethods.WS styleNotContains = 0,
			NativeMethods.WS_EX styleExContains = 0, NativeMethods.WS_EX styleExNotContains = 0,
			bool isManaged = true, int windowCreatedDelay = 350, bool switchToOnCreated = true, IList<Rule> rules = null)
		{
			this.className = new Regex(className, RegexOptions.Compiled);
			this.displayName = new Regex(displayName, RegexOptions.Compiled);
			this.styleContains = styleContains;
			this.styleNotContains = styleNotContains;
			this.styleExContains = styleExContains;
			this.styleExNotContains = styleExNotContains;
			this.isManaged = isManaged;
			this.windowCreatedDelay = windowCreatedDelay;
			this.switchToOnCreated = switchToOnCreated;
			if (isManaged)
			{
				this.rules = rules == null ? new Rule[] { new Rule() } : rules.ToArray();
			}
		}

		public class Rule
		{
			public Rule(int workspace = 0, bool isFloating = false, bool showInTabs = true,
				State titlebar = State.AS_IS, State inTaskbar = State.AS_IS, State windowBorders = State.AS_IS,
				bool redrawOnShow = false)
			{
				this.workspace = workspace;
				this.isFloating = isFloating;
				this.showInTabs = showInTabs;
				this.titlebar = titlebar;
				this.inTaskbar = inTaskbar;
				this.windowBorders = windowBorders;
				this.redrawOnShow = redrawOnShow;
			}

			internal readonly int workspace;
			internal readonly bool isFloating;
			internal readonly bool showInTabs;
			internal readonly State titlebar;
			internal readonly State inTaskbar;
			internal readonly State windowBorders;
			internal readonly bool redrawOnShow;
		}
	}
}
