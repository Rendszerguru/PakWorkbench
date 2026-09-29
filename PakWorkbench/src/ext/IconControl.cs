using System;
using System.Collections.Generic;
using System.Drawing;

namespace PakWorkbench.src.ext
{
	public static class IconControl
	{
		private static float _currentDpi = 1.0f;
		private static readonly Dictionary<string, ICustomIcon> _icons = new(StringComparer.OrdinalIgnoreCase);

		static IconControl()
		{
			RegisterIcon(".acp", new AcpIcon());

			var aeIcon = new AdobeEffectsIcon();
			RegisterIcon(".ae", aeIcon);
			RegisterIcon(".ast", aeIcon);
			RegisterIcon(".aex", aeIcon);
			RegisterIcon(".asy", aeIcon);

			RegisterIcon(".agf", new AgfIcon());
			RegisterIcon(".agr", new AgrIcon());
			RegisterIcon(".anm", new AnmIcon());
			RegisterIcon(".asi", new AsiIcon());
			RegisterIcon(".aw", new AwIcon());
			RegisterIcon(".bt", new BtIcon());
			RegisterIcon(".c", new CIcon());
			RegisterIcon(".ct", new CtIcon());

			var mapIcon = new MapIcon();
			RegisterIcon(".cso", mapIcon);
			RegisterIcon(".smd", mapIcon);
			RegisterIcon(".ntile", mapIcon);
			RegisterIcon(".ttile", mapIcon);
			RegisterIcon(".pso", mapIcon);
			RegisterIcon(".smap", mapIcon);
			RegisterIcon(".terr", mapIcon);
			RegisterIcon(".vso", mapIcon);
			RegisterIcon(".map", mapIcon);
			RegisterIcon(".svg", mapIcon);
			RegisterIcon(".bterr", mapIcon);
			RegisterIcon(".desc", mapIcon);

			RegisterIcon(".dds", new DdsIcon());
			RegisterIcon(".edds", new DdsIcon());
			RegisterIcon(".emat", new EmatIcon());
			RegisterIcon(".ent", new EntIcon());
			RegisterIcon(".et", new EtIcon());
			RegisterIcon(".fnt", new FntIcon());
			RegisterIcon(".layer", new LayerIcon());
			RegisterIcon(".layout", new LayoutIcon());
			RegisterIcon(".pap", new PapIcon());
			RegisterIcon(".siga", new SigaIcon());

			var gProjMatIcon = new GProjectMaterialIcon();
			RegisterIcon(".physmat", gProjMatIcon);
			RegisterIcon(".vhcsurf", gProjMatIcon);
			RegisterIcon(".gamemat", gProjMatIcon);
			RegisterIcon(".conf", gProjMatIcon);
			RegisterIcon(".gproj", gProjMatIcon);

			RegisterIcon(".ptc", new PtcIcon());

			var ragdollTextIcon = new RagdollTextIcon();
			RegisterIcon(".ragdoll", ragdollTextIcon);
			RegisterIcon(".txt", ragdollTextIcon);

			var sigAfmIcon = new SigAfmIcon();
			RegisterIcon(".sig", sigAfmIcon);
			RegisterIcon(".afm", sigAfmIcon);

			RegisterIcon(".st", new StIcon());
			RegisterIcon(".stars", new StarsIcon());
			RegisterIcon(".styles", new StylesIcon());

			var topoNmnIcon = new TopoNmnIcon();
			RegisterIcon(".topo", topoNmnIcon);
			RegisterIcon(".nmn", topoNmnIcon);

			RegisterIcon(".txa", new TxaIcon());

			var wavSndIcon = new WavSndIcon();
			RegisterIcon(".wav", wavSndIcon);
			RegisterIcon(".snd", wavSndIcon);

			var xobFbxTxoIcon = new XobFbxTxoIcon();
			RegisterIcon(".xob", xobFbxTxoIcon);
			RegisterIcon(".fbx", xobFbxTxoIcon);
			RegisterIcon(".txo", xobFbxTxoIcon);
		}

		public static void RegisterIcon(string extension, ICustomIcon icon)
		{
			icon.UpdateDpi(_currentDpi);
			_icons[extension] = icon;
		}

		public static void UpdateDpiScale(float dpiScale)
		{
			_currentDpi = dpiScale;
			foreach (var icon in _icons.Values)
			{
				icon.UpdateDpi(dpiScale);
			}
		}

		public static void ParseNodeData(string rawText, out string displayText, out string iconPart, out Color? iconColor, out ICustomIcon? customIcon, out bool isFolder)
		{
			iconPart = string.Empty;
			iconColor = null;
			customIcon = null;
			isFolder = false;
			displayText = rawText ?? string.Empty;

			if (displayText.StartsWith("📁 "))
			{
				iconPart = "📁 ";
				displayText = displayText[3..];
				iconColor = UITheme.FolderIcon;
				isFolder = true;
			}
			else if (displayText.StartsWith("📄 "))
			{
				iconPart = "📄 ";
				displayText = displayText[3..];

				foreach (var kvp in _icons)
				{
					if (displayText.EndsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
					{
						customIcon = kvp.Value;
						iconColor = customIcon.TextColor;
						return;
					}
				}

				iconColor = UITheme.SyntaxString;
			}
		}
	}
}