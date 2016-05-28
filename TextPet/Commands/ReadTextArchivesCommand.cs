﻿using LibTextPet.IO.Msg;
using LibTextPet.IO.TextBox;
using LibTextPet.IO.TPL;
using LibTextPet.Msg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TextPet.Commands {
	/// <summary>
	/// A command line interface command that reads text archives from files.
	/// </summary>
	internal class ReadTextArchivesCommand : CliCommand {
		public override string Name => "read-text-archives";
		public override string RunString {
			get {
				this.Cli.SetObjectNames("text archive", null);
				return null;
			}
		}

		private const string formatArg = "format";
		private const string pathArg = "path";
		private const string recursiveArg = "recursive";

		private readonly string[] binFormats = new string[] {
			"BIN", "BINARY", "DMP", "DUMP", "MSG", "MESSAGE",
		};
		private readonly string[] tplFormats = new string[] {
			"TPL", "TEXTPET", "TEXTPETLANGUAGE",
		};
		private readonly string[] txtFormats = new string[] {
			"TXT", "TEXT", "TEXTS", "BOX", "BOXES", "TEXTBOX", "TEXTBOXES",
		};
		private readonly string[] romFormats = new string[] {
			"ROM", "READONLYMEMORY", "GBA", "AGB", "GAMEBOYADVANCE",
		};

		protected bool Recursive { get; set; }

		public ReadTextArchivesCommand(CommandLineInterface cli, TextPetCore core)
			: base(cli, core, new string[] {
				pathArg,
			}, new OptionalArgument[] {
				new OptionalArgument(formatArg, 'f', "format"),
				new OptionalArgument(recursiveArg, 'r'),
			}) {
			this.Recursive = false;
		}

		protected override void RunImplementation() {
			string manualFormat = GetOptionalValues(formatArg)?[0];
			string path = GetRequiredValue(pathArg);
			this.Recursive = GetOptionalValues(recursiveArg) != null;

			// If format is not specified, use file extension.
			string format;
			if (manualFormat != null) {
				format = manualFormat;
			} else {
				string extension = Path.GetExtension(path);

				if (extension.Length <= 1 || extension[0] != '.') {
					Console.WriteLine("ERROR: Text archive format not specified.");
					return;
				}

				format = extension.Substring(1);
			}

			format = format.ToUpperInvariant().Replace("-", "");

			if (binFormats.Contains(format)) {
				ReadTextArchivesBinary(path);
			} else if (tplFormats.Contains(format)) {
				this.ReadTextArchivesTPL(path);
			} else if (txtFormats.Contains(format)) {
				this.ReadTextArchivesTextBoxes(path);
			} else if (romFormats.Contains(format)) {
				this.Core.ReadTextArchivesROM(path);
			} else if (manualFormat == null) {
				Console.WriteLine("ERROR: Unknown text archive extension \"" + format + "\". Change the file extension or specify the format manually.");
			} else {
				Console.WriteLine("ERROR: Unknown text archive format \"" + format + "\".");
			}
		}

		/// <summary>
		/// Verifies whether the active game has been initialized, printing an error an exception if it has not.
		/// </summary>
		protected bool VerifyGameInitialized() {
			if (this.Core.Game == null) {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("ERROR: No active game has been set.");
				Console.ResetColor();
				return false;
			} else {
				return true;
			}
		}

		/// <summary>
		/// Reads binary text archives from the specified path.
		/// </summary>
		/// <param name="path">The path to load from. Can be a file or folder.</param>
		public void ReadTextArchivesBinary(string path) {
			this.Core.ReadTextArchives(path, this.Recursive, delegate (MemoryStream ms, string file) {
				BinaryTextArchiveReader reader = new BinaryTextArchiveReader(ms, this.Core.Game);
				reader.IgnorePointerSyncErrors = true;

				TextArchive ta = reader.Read((int)ms.Length);

				if (ta == null) {
					throw new InvalidDataException("Could not read text archive " + Path.GetFileName(file) + ".");
				}

				ta.Identifier = Path.GetFileNameWithoutExtension(file);
				return new TextArchive[] { ta };
			});
		}

		/// <summary>
		/// Reads TextPet Language text archives from the specified path.
		/// </summary>
		/// <param name="path">The path to load from. Can be a file or folder.</param>
		public void ReadTextArchivesTPL(string path) {
			if (!VerifyGameInitialized()) {
				return;
			}
			CommandDatabase[] databases = this.Core.Game.Databases.ToArray();

			this.Core.ReadTextArchives(path, this.Recursive, delegate (MemoryStream ms, string file) {
				TPLTextArchiveReader reader = new TPLTextArchiveReader(ms, databases);

				List<TextArchive> tas = new List<TextArchive>();
				while (!reader.AtEnd) {
					IEnumerable<TextArchive> readTAs = reader.Read();

					foreach (TextArchive ta in readTAs) {
						if (ta == null) {
							throw new InvalidDataException("Could not read one or more text archives from " + Path.GetFileName(file) + ".");
						}
					}

					tas.AddRange(readTAs);
				}
				return tas;
			});
		}

		/// <summary>
		/// Reads text box template text archives from the specified path.
		/// </summary>
		/// <param name="path">The path to load from. Can be a file or folder.</param>
		/// <param name="recursive">Whether the files should be read recursively, in case of a folder read.</param>
		public void ReadTextArchivesTextBoxes(string path) {
			if (!VerifyGameInitialized()) {
				return;
			}
			CommandDatabase[] databases = this.Core.Game.Databases.ToArray();

			this.Core.ReadTextArchives(path, this.Recursive, delegate (MemoryStream ms, string file) {
				TextBoxTextArchiveTemplateReader reader = new TextBoxTextArchiveTemplateReader(ms, databases);

				List<TextArchive> tas = new List<TextArchive>();
				while (!reader.AtEnd) {
					IEnumerable<TextArchive> readTAs = reader.Read();

					foreach (TextArchive ta in readTAs) {
						if (ta == null) {
							throw new InvalidDataException("Could not read one or more text archives from " + Path.GetFileName(file) + ".");
						}
					}

					tas.AddRange(readTAs);
				}
				return tas;
			});
		}
	}
}
