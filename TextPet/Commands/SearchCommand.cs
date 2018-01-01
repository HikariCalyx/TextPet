﻿using LibTextPet.General;
using LibTextPet.IO.Msg;
using LibTextPet.Msg;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TextPet.Commands {
	/// <summary>
	/// A command line interface command that searches a file for all text archives.
	/// </summary>
	internal class SearchCommand : CliCommand {
		public override string Name => "search";
		public override string RunString {
			get {
				this.Cli.SetObjectNames("text archive", null);
				return "Searching file...";
			}
		}

		private const string pathArg = "path";

		private const string startArg = "start";
		private const string offsetArg = "offset";
		private const string lengthArg = "length";
		private const string deepArg = "deep";
		private const string minSizeArg = "minimum-size";
		private const string sizeArg = "size";
		private const string noRecordArg = "no-record";
		private const string prependFileNameArg = "prepend-file-name";

		public SearchCommand(CommandLineInterface cli, TextPetCore core)
			: base(cli, core, new string[] {
				pathArg,
			}, new OptionalArgument[] {
				new OptionalArgument(startArg, 's', offsetArg),
				new OptionalArgument(lengthArg, 'l', lengthArg),
				new OptionalArgument(deepArg, 'd'),
				new OptionalArgument(minSizeArg, 'm', sizeArg),
				new OptionalArgument(noRecordArg, 'n'),
				new OptionalArgument(prependFileNameArg, 'p'),
			}) { }

		protected override void RunImplementation() {
			string path = GetRequiredValue(pathArg);
			string startArg = GetOptionalValues(SearchCommand.startArg)?[0];
			string lengthArg = GetOptionalValues(SearchCommand.lengthArg)?[0];
			string minSizeArg = GetOptionalValues(SearchCommand.minSizeArg)?[0];
			bool deep = GetOptionalValues(deepArg) != null;
			bool noRecord = GetOptionalValues(noRecordArg) != null;
			bool prependFileName = GetOptionalValues(prependFileNameArg) != null;

			this.Core.LoadFile(path);

			long start = 0;
			if (startArg != null) {
				start = Math.Max(0, NumberParser.ParseInt64(startArg));
			}

			long length = this.Core.LoadedFile.Length;
			if (lengthArg != null) {
				length = Math.Max(0, NumberParser.ParseInt64(lengthArg));
			}
			length = Math.Min(this.Core.LoadedFile.Length - start, length);

			int minSize = 0;
			if (minSizeArg != null) {
				minSize = Math.Max(0, NumberParser.ParseInt32(minSizeArg));
			}

			FileTextArchiveReader reader = new FileTextArchiveReader(this.Core.LoadedFile, this.Core.Game, this.Core.FileIndex) {
				CheckGoodTextArchive = !deep,
				MinimumSize = minSize,
				UpdateFileIndex = !noRecord
			};
			reader.TextArchiveReader.IgnorePointerSyncErrors = true;
			reader.TextArchiveReader.AutoSortPointers = false;

			int found = 0;
			
			for (long p = 0; p < length; p += 4) {
				long offset = start + p;
				offset &= ~0x3;

				if (this.Cli.Verbose) {
					PrintProgress(length, found, p, offset);
				}

				this.Core.LoadedFile.Position = offset;
				TextArchive ta = reader.Read();

				if (ta != null) {
					if (prependFileName) {
						if (offset == 0) {
							ta.Identifier = Path.GetFileNameWithoutExtension(path);
						} else {
							ta.Identifier = Path.GetFileNameWithoutExtension(path) + '_' + ta.Identifier;
						}
					}

					this.Core.TextArchives.Add(ta);
					found++;
				}
			}
			PrintProgress(length, found, length, start+length);
			Console.WriteLine();
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "System.Console.Write(System.String)")]
		private static void PrintProgress(long length, int found, long p, long offset) {
			int percentage = (int)(100 * p / length);
			if (percentage > 100) {
				percentage = 100;
			}

			Console.Write("\rSearching... ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(offset.ToString("X6", CultureInfo.InvariantCulture));
			Console.ResetColor();
			Console.Write(" (");
			if (Console.BackgroundColor == ConsoleColor.White) {
				Console.ForegroundColor = ConsoleColor.Black;
			} else {
				Console.ForegroundColor = ConsoleColor.White;
			}
			Console.Write(percentage.ToString(CultureInfo.InvariantCulture).PadLeft(2));
			if (percentage < 100) {
				Console.Write("%");
			}
			Console.ResetColor();
			Console.Write(") (");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(found);
			Console.ResetColor();
			Console.Write(" found)");
		}
	}
}
