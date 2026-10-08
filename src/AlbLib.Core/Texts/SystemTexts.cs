/* PORT: minimal replacement for AlbLib's SysTextRepository/GameData.SystemTexts (repository layer not ported). */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AlbLib.Texts
{
	/// <summary>
	/// Reads the SYSTEXTS file (lines in the form <c>[0203:Text]</c>).
	/// </summary>
	public static class SystemTexts
	{
		public static readonly Regex LineRegex = new Regex(@"\[([0-9]{4}):(.*?)\]", RegexOptions.Compiled);
		
		private static readonly object sync = new object();
		private static Dictionary<int,string> cache;
		private static string cachePath;
		
		/// <summary>
		/// Returns the system text with the given id, or null when unavailable.
		/// </summary>
		public static string Get(int id)
		{
			var texts = Load();
			if(texts == null) return null;
			string value;
			return texts.TryGetValue(id, out value) ? value : null;
		}
		
		/// <summary>
		/// PORT: parses SYSTEXTS content from a stream.
		/// </summary>
		public static Dictionary<int,string> Read(Stream stream)
		{
			var dict = new Dictionary<int,string>();
			using(var reader = new StreamReader(stream, TextCore.DefaultEncoding, false, 4096, true))
			{
				string line;
				while((line = reader.ReadLine()) != null)
				{
					foreach(Match match in LineRegex.Matches(line))
					{
						dict[Int32.Parse(match.Groups[1].Value)] = match.Groups[2].Value;
					}
				}
			}
			return dict;
		}

		/// <summary>
		/// PORT: uses in-memory texts instead of reading <see cref="Paths.SystemTexts"/>.
		/// </summary>
		public static void SetTexts(Dictionary<int,string> texts)
		{
			lock(sync)
			{
				cache = texts;
				cachePath = InMemory;
			}
		}

		private const string InMemory = "<memory>";

		public static void ClearCache()
		{
			lock(sync)
			{
				cache = null;
				cachePath = null;
			}
		}
		
		private static Dictionary<int,string> Load()
		{
			lock(sync)
			{
				if(cachePath == InMemory) return cache;
			}
			if(Paths.SystemTexts == null) return null;
			string path = Paths.SystemTexts.WithDefaultLanguage();
			lock(sync)
			{
				if(cache != null && cachePath == path) return cache;
				if(!File.Exists(path)) return null;
				var dict = new Dictionary<int,string>();
				foreach(string line in File.ReadLines(path, TextCore.DefaultEncoding))
				{
					foreach(Match match in LineRegex.Matches(line))
					{
						dict[Int32.Parse(match.Groups[1].Value)] = match.Groups[2].Value;
					}
				}
				cache = dict;
				cachePath = path;
				return dict;
			}
		}
	}
}
