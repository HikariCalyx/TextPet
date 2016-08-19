﻿using LibTextPet.General;
using LibTextPet.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TextPet.Commands {
	internal class WriteROMEntriesCommand : CliCommand {
		public override string Name => "write-rom-entries";
		public override string RunString => "Writing ROM entries...";

		private const string pathArg = "path";

		private const string commentsArg = "comments";
		private const string bytesArg = "bytes";
		private const string addSizeArg = "add-size";
		private const string amountArg = "amount";
		private const string excludeByteArg = "exclude-byte";
		private const string byteArg = "byte";

		public WriteROMEntriesCommand(CommandLineInterface cli, TextPetCore core)
			: base(cli, core, new string[] {
				pathArg,
			}, new OptionalArgument[] {
				new OptionalArgument(commentsArg, 'c'),
				new OptionalArgument(bytesArg, 'b'),
				new OptionalArgument(addSizeArg, 'a', new string[] {
					amountArg,
				}),
				new OptionalArgument(excludeByteArg, 'e', new string[] {
					byteArg,
				}),
			}) {
		}

		protected override void RunImplementation() {
			string path = GetRequiredValue(pathArg);
			bool comments = GetOptionalValues(commentsArg) != null;
			bool bytes = GetOptionalValues(bytesArg) != null;
			string addSizeStr = GetOptionalValues(addSizeArg)?[0];
			int addSize = addSizeStr != null ? NumberParser.ParseInt32(addSizeStr) : 0;
			string excludeByteStr = GetOptionalValues(excludeByteArg)?[0];
			int excludeByte = excludeByteStr != null ? NumberParser.ParseInt32(excludeByteStr) : -1;

			using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) {
				ROMEntriesWriter writer = new ROMEntriesWriter(fs);
				writer.AddSize = addSize;
				writer.ExcludeByte = excludeByte;
				writer.IncludeFormatComments = true;

				if (comments) {
					writer.IncludeGapComments = true;
					writer.IncludeOverlapComments = true;
					writer.IncludePointerWarnings = true;
				}
				if (bytes) {
					writer.IncludePostBytesComments = true;
				}

				writer.Write(this.Core.ROMEntries, this.Core.ROM);
			}
		}
	}
}
