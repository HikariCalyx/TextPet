﻿using LibTextPet.IO.TPL;
using LibTextPet.Msg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LibTextPet.IO.TextBox {
	public class TextBoxScriptTemplateReader : TextBoxTemplateReader<Script> {
		public TextBoxScriptTemplateReader(Stream stream, params CommandDatabase[] databases)
			: base(stream, databases) { }

		/// <summary>
		/// Initializes a script to be read from the token enumerator.
		/// </summary>
		/// <returns>The initialized script.</returns>
		protected override Script BeginRead() {
			return new Script();
		}

		/// <summary>
		/// Finalized the script that was read.
		/// </summary>
		/// <param name="obj">The script that was read.</param>
		/// <param name="db">The command database that was used.</param>
		/// <returns>The finalized script.</returns>
		protected override Script EndRead(Script obj, CommandDatabase db) {
			if (obj != null && db != null) {
				obj.DatabaseName = db.Name;
			}

			return base.EndRead(obj, db);
		}

		protected override ProcessResult ProcessToken(Script obj, Token token, CommandDatabase db) {
			if (obj == null)
				throw new ArgumentNullException(nameof(obj), "The script cannot be null.");
			if (db == null)
				throw new ArgumentNullException(nameof(db), "The command database cannot be null.");

			switch (token.Class) {
			case (int)TextBoxTokenType.Directive:
				DirectiveElement directive = ParseDirective(token);
				switch (directive.DirectiveType) {
				case DirectiveType.TextArchive:
					return ProcessResult.Stop;
				default:
					obj.Add(directive);
					return ProcessResult.ConsumeAndContinue;
				}
			case (int)TextBoxTokenType.Command:
				CommandDefinition def = db[token.Value].FirstOrDefault();
				if (def is null) {
					return new ProcessResult(null, false, false);
				}
				obj.Add(new Command(def));
				return ProcessResult.ConsumeAndContinue;
			case (int)TextBoxTokenType.Text:
				obj.Add(new TextElement(token.Value
					// TODO: This sucks
					.Replace("\\<", "<")
					.Replace("\\>", ">")
					.Replace("\\\\", "\\")
					.Replace("\r", "")
				));
				return ProcessResult.ConsumeAndContinue;
			case (int)TextBoxTokenType.Comment:
				return ProcessResult.ConsumeAndContinue;
			}
			throw new ArgumentException("Unrecognized token.", nameof(token));
		}
	}
}
