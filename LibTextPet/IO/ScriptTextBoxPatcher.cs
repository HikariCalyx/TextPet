﻿using LibTextPet.General;
using LibTextPet.IO.Msg;
using LibTextPet.Msg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LibTextPet.IO {
	/// <summary>
	/// An patcher that patches new text boxes into base scripts.
	/// </summary>
	public class ScriptTextBoxPatcher : IPatcher<Script> {
		/// <summary>
		/// Gets the scripts that are used for text box splitting.
		/// </summary>
		protected ReadOnlyNamedCollection<CommandDatabase> Databases { get; }

		/// <summary>
		/// Creates a new text box patcher that uses the specified command databases.
		/// </summary>
		/// <param name="databases">The command databases to use.</param>
		public ScriptTextBoxPatcher(params CommandDatabase[] databases) {
			if (databases == null)
				throw new ArgumentNullException(nameof(databases), "The command databases cannot be null.");

			this.Databases = new ReadOnlyNamedCollection<CommandDatabase>(databases);
		}

		/// <summary>
		/// Patches the text boxes from the specified patch script into the specified base script.
		/// </summary>
		/// <param name="baseObj">The base script to patch new text boxes into.</param>
		/// <param name="patchObj">The patch script containing new text boxes.</param>
		public void Patch(Script baseObj, Script patchObj) {
			if (baseObj == null)
				throw new ArgumentNullException(nameof(baseObj), "The base script cannot be null.");
			if (patchObj == null)
				throw new ArgumentNullException(nameof(patchObj), "The patch script cannot be null.");

			IList<Command> boxA;
			Script boxB;

			// Load the text box split script, if there is one.
			Script textBoxSplitScript = GetTextBoxSplitScript(baseObj);

			int a = 0;
			int b = 0;
			while (a < baseObj.Count) {
				// Skip any elements that are not part of a text box.
				if (!IsPrinted(baseObj[a])) {
					a++;
					continue;
				}

				// Start of a new text box.
				// Advance B to the next text box.
				b = FindNextTextBox(patchObj, b);

				// Do we have text left in B?
				if (b >= patchObj.Count) {
					throw new ArgumentException("The patch script must have the same number of text boxes as the base script.", nameof(patchObj));
				}

				// Extract text box from B.
				boxB = RemoveSplitTextBox(patchObj, textBoxSplitScript, b);

				// If box B is empty, merge the current and next text box in the base script.
				if (!boxB.Any()) {
					// Do we have a split script?
					if (textBoxSplitScript == null) {
						throw new ArgumentException("Command database \"" + baseObj.DatabaseName + "\" has no text box split script; text box merging is not supported.", nameof(patchObj));
					}

					MergeNextTextBox(baseObj, textBoxSplitScript, a);
				} else {
					// Remove the old text box from A.
					boxA = RemoveTextBoxCommands(baseObj, a);

					// Patch box B commands with commands in A.
					PatchTextBox(boxB, boxA);

					// Re-insert the patched text box in A.
					foreach (IScriptElement elem in boxB) {
						baseObj.Insert(a++, elem);
					}
				}
			}
		}

		/// <summary>
		/// Extracts all script commands from the current text box in the specified script, and discards any other commands from the script.
		/// </summary>
		/// <param name="script">The script to extract from.</param>
		/// <param name="index">The index at which to begin extracting.</param>
		/// <returns>The extracted script commands.</returns>
		private static IList<Command> RemoveTextBoxCommands(Script script, int index) {
			// Extract commands from next text box.
			IList<Command> boxA = new List<Command>();
			while (index < script.Count && !EndsTextBox(script[index])) {
				// We only need to copy the commands, so discard the other elements.
				Command cmdA = script[index] as Command;
				if (cmdA != null) {
					// Extract the command from A.
					boxA.Add(cmdA);
				}
				// Discard the printed element.
				script.RemoveAt(index);
			}

			return boxA;
		}

		/// <summary>
		/// Extracts a (possibly split) text box from the specified script at the specified index, removing the elements in the script.
		/// </summary>
		/// <param name="script">The script to extract a text box from.</param>
		/// <param name="textBoxSplitScript">The text box split script to use, if the text box is split.</param>
		/// <param name="index">The index to start extracting from.</param>
		/// <returns>The text box that was extracted.</returns>
		private static Script RemoveSplitTextBox(Script script, Script textBoxSplitScript, int index) {
			Script boxB = new Script();
			while (index < script.Count && !EndsTextBox(script[index])) {
				if (SplitsTextBox(script[index])) {
					// Do we have a split script?
					if (textBoxSplitScript == null) {
						throw new ArgumentException("No text box split script provided; text box splitting is not supported.", nameof(script));
					}

					// Apply the text box split script to extend the current text box.
					foreach (IScriptElement elem in textBoxSplitScript) {
						boxB.Add(elem);
					}
				} else if (IsPrinted(script[index])) {
					// Only extract those elements that are actually printed.
					boxB.Add(script[index]);
				}
				script.RemoveAt(index);
			}

			return boxB;
		}

		/// <summary>
		/// Merges the next two text boxes in the specified script, by removing the script elements in the specified text box split script.
		/// </summary>
		/// <param name="script">The script to modify.</param>
		/// <param name="textBoxSplitScript">The text box split script to use.</param>
		/// <param name="index">The index at which to begin merging.</param>
		private static void MergeNextTextBox(Script script, Script textBoxSplitScript, int index) {
			// Do we have a split script?
			if (textBoxSplitScript == null) {
				throw new ArgumentNullException(nameof(textBoxSplitScript), "The text box split script cannot be null.");
			}

			// Extract the next few commands from the base object.
			Script nextCommands = new Script(script.DatabaseName);
			for (int i = 0, j = 0; i < textBoxSplitScript.Count && (i + j + index) < script.Count; i++) {
				// Skip printed commands (j is used as skipped counter).
				if (IsPrinted(script[i + j + index])) {
					j++;
					i--;
					continue;
				}
				nextCommands.Add(script[i + j + index]);
			}

			// Check if equal.
			if (!Enumerable.SequenceEqual(nextCommands, textBoxSplitScript)) {
				throw new ArgumentException("Next commands following empty text box do not match text box split script. Could not merge text boxes.", nameof(script));
			}

			// Merge with the next text box by removing the split script commands.
			for (int i = 0, j = 0; i < textBoxSplitScript.Count; i++) {
				// Don't discard printed commands.
				if (IsPrinted(script[index + j])) {
					j++;
					i--;
				} else {
					script.RemoveAt(index + j);
				}
			}
		}

		/// <summary>
		/// Finds the script command index of the start of the next text box in the specified script, starting from the specified position.
		/// </summary>
		/// <param name="script">The script to search.</param>
		/// <param name="start">The index to begin searching from.</param>
		/// <returns>The index of the start of the next text box, or the size of the script if no next text box was found.</returns>
		private static int FindNextTextBox(Script script, int start) {
			while (start < script.Count) {
				if (IsPrinted(script[start])) {
					break;
				}

				DirectiveElement directive = script[start++] as DirectiveElement;
				if (directive != null && directive.DirectiveType == DirectiveType.TextBoxSeparator) {
					break;
				}
			}

			return start;
		}

		/// <summary>
		/// Gets the text box split script that is to be used for the specified script.
		/// </summary>
		/// <param name="script">The script to get the text box split script for.</param>
		/// <returns>The text box split script, or null if none was found.</returns>
		private Script GetTextBoxSplitScript(Script script) {
			Script textBoxSplitScript = null;
			if (this.Databases.Contains(script.DatabaseName)) {
				// Load the database.
				CommandDatabase db = this.Databases[script.DatabaseName];
				if (db.TextBoxSplitScript != null && db.TextBoxSplitScript.Any()) {
					textBoxSplitScript = db.TextBoxSplitScript;
				}
			}

			return textBoxSplitScript;
		}

		/// <summary>
		/// Replaces all script commands in the specified text box with commands in the specified list of commands.
		/// </summary>
		/// <param name="box">The text box.</param>
		/// <param name="commands">The commands to replace with.</param>
		private static void PatchTextBox(Script box, IList<Command> commands) {
			for (int b = 0; b < box.Count; b++) {
				Command cmdB = box[b] as Command;
				if (cmdB == null) {
					continue;
				}

				// Find corresponding command in A.
				bool found = false;
				for (int a = 0; a < commands.Count; a++) {
					Command cmdA = commands[a];
					if (cmdA.Name.Equals(cmdB.Name, StringComparison.OrdinalIgnoreCase)) {
						// Replace the placeholder command in B.
						box[b] = cmdA;
						// Remove the command from A.
						commands.RemoveAt(a);
						// Found the command in A.
						found = true;
						break;
					}
				}
				if (!found) {
					throw new ArgumentException("Could not find a corresponding command in the base script for command \"" + cmdB.Name + "\" in the text script.", nameof(box));
				}
			}
			// Check if all commands were put in.
			if (commands.Any()) {
				throw new ArgumentException("Not all commands are accounted for in the text box.", nameof(box));
			}
		}

		/// <summary>
		/// Checks whether the specified script element prints to the screen.
		/// </summary>
		/// <param name="element">The element to check.</param>
		/// <returns>true if the element prints to the screen; otherwise, false.</returns>
		protected static bool IsPrinted(IScriptElement element) {
			if (element == null)
				throw new ArgumentNullException(nameof(element), "The script element cannot be null.");

			// Treat text and raw byte as part of text box.
			if (element is TextElement || element is ByteElement) {
				return true;
			}

			// Treat printable command as part of text box.
			Command cmd = element as Command;
			if (cmd != null && cmd.Definition.Prints) {
				return true;
			}

			return false;
		}

		/// <summary>
		/// Checks whether the specified script element ends the current text box. This is the case if the element is a non-printing command,
		/// a text box separator, or the start of a new script or text archive.
		/// </summary>
		/// <param name="element">The element to check.</param>
		/// <returns>true if the element ends the current text box; otherwise, false.</returns>
		protected static bool EndsTextBox(IScriptElement element) {
			if (element == null)
				throw new ArgumentNullException(nameof(element), "The script element cannot be null.");

			Command cmd = element as Command;
			if (cmd != null && !cmd.Definition.Prints) {
				return true;
			}

			DirectiveElement directive = element as DirectiveElement;
			if (directive != null && (
				directive.DirectiveType == DirectiveType.TextBoxSeparator ||
				directive.DirectiveType == DirectiveType.Script ||
				directive.DirectiveType == DirectiveType.TextArchive
			)) {
				return true;
			}

			return false;
		}

		/// <summary>
		/// Checks whether the specified element splits the current text box.
		/// </summary>
		/// <param name="element">The element to check.</param>
		/// <returns>true if the element splits the current text box; otherwise, false.</returns>
		protected static bool SplitsTextBox(IScriptElement element) {
			if (element == null)
				throw new ArgumentNullException(nameof(element), "The script element cannot be null.");

			DirectiveElement directive = element as DirectiveElement;
			if (directive != null && directive.DirectiveType == DirectiveType.TextBoxSplit) {
				return true;
			} else {
				return false;
			}
		}
	}
}
