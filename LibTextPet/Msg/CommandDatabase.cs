﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LibTextPet.General;
using LibTextPet.Plugins;

namespace LibTextPet.Msg {
	/// <summary>
	/// A database of script command definition.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
	public class CommandDatabase : IPlugin, IEnumerable<CommandDefinition>, INameable {
		public string PluginType => "command database";

		/// <summary>
		/// The commands in this database.
		/// </summary>
		private List<CommandDefinition> definitions;
		/// <summary>
		/// An index mapping for the command names in this database.
		/// </summary>
		private Dictionary<string, List<CommandDefinition>> nameMap;
		/// <summary>
		/// A cached read-only collection of all definitions in this command database.
		/// </summary>
		private ReadOnlyCollection<CommandDefinition> readOnlyDefinitions = null;

		/// <summary>
		/// Gets the last results obtained from a database query.
		/// </summary>
		private ICollection<CommandDefinition> LastResults { get; set; }
		/// <summary>
		/// Gets the last byte sequence used in a database query.
		/// </summary>
		private IList<byte> LastSequence { get; set; }

		/// <summary>
		/// Gets the script snippet used for splitting text boxes.
		/// </summary>
		public Script TextBoxSplitSnippet { get; internal set; }

		/// <summary>
		/// Gets the name of this command database.
		/// </summary>
		public string Name { get; private set; }

		/// <summary>
		/// Creates an empty command database with the specified name.
		/// </summary>
		/// <param name="name">The name of this command database.</param>
		public CommandDatabase(string name) {
			if (name == null)
				throw new ArgumentNullException(nameof(name), "The database name cannot be null.");
			if (String.IsNullOrWhiteSpace(name))
				throw new ArgumentException("The database name cannot be empty.", nameof(name));

			this.Name = name;

			this.definitions = new List<CommandDefinition>();
			this.nameMap = new Dictionary<string, List<CommandDefinition>>();
		}

		/// <summary>
		/// Gets all commands with the given name in this database.
		/// </summary>
		/// <param name="name">The name to search for; case-insensitive.</param>
		/// <returns>The commands found.</returns>
		public IList<CommandDefinition> this[string name] {
			get {
				if (name == null)
					throw new ArgumentNullException(nameof(name), "The name cannot be null.");
				if (name.Length <= 0)
					throw new ArgumentException("The name cannot be empty.", nameof(name));

				return this.Find(name);
			}
		}

		/// <summary>
		/// Gets all commands in this database.
		/// </summary>
		public IList<CommandDefinition> Commands {
			get {
				if (this.readOnlyDefinitions == null) {
					// If uncached, cache the current definitions in the command database as a read-only array.
					this.readOnlyDefinitions = new ReadOnlyCollection<CommandDefinition>(this.readOnlyDefinitions.ToArray());
				}
				return this.readOnlyDefinitions;
			}
		}

		/// <summary>
		/// Adds the given command definition to the database.
		/// </summary>
		/// <param name="definition">The command definition to add.</param>
		public void Add(CommandDefinition definition) {
			if (definition == null)
				throw new ArgumentNullException(nameof(definition), "The command cannot be null.");

			// Add the command to the commands list.
			this.definitions.Add(definition);

			// Add the command to the name map.
			string name = definition.Name.ToUpperInvariant();
			List<CommandDefinition> defs;
			if (!this.nameMap.TryGetValue(name, out defs)) {
				defs = new List<CommandDefinition>();
			}
			defs.Add(definition);
			this.nameMap[name] = defs;

			// Delete the cached read-only definitions collection.
			this.readOnlyDefinitions = null;
		}

		/// <summary>
		/// Finds all commands with the given name in this database.
		/// </summary>
		/// <param name="name">The name to search for; case-insensitive.</param>
		/// <returns>The commands found.</returns>
		public IList<CommandDefinition> Find(string name) {
			if (name == null)
				throw new ArgumentNullException(nameof(name), "The name cannot be null.");
			if (name.Length <= 0)
				throw new ArgumentException("The name cannot be empty.", nameof(name));

			List<CommandDefinition> result;

			// Find all command definitions with the given name.
			List<CommandDefinition> defs;
			if (this.nameMap.TryGetValue(name.ToUpperInvariant(), out defs)) {
				result = new List<CommandDefinition>(defs.Count);
				foreach (CommandDefinition def in defs) {
					result.Add(def);
				}
			} else {
				result = new List<CommandDefinition>(0);
			}

			return result;
		}

		/// <summary>
		/// Caches the given command definition results for the given sequence lookup.
		/// </summary>
		/// <param name="sequence">The byte sequence that was matched.</param>
		/// <param name="results">The resulting command definitions.</param>
		private void Cache(IList<byte> sequence, ICollection<CommandDefinition> results) {
			if (sequence == null)
				throw new ArgumentNullException(nameof(sequence), "The byte sequence cannot be null.");
			if (results == null)
				throw new ArgumentNullException(nameof(results), "The results cannot be null.");

			// If the sequence is empty or there are no results, it makes no sense to cache.
			if (sequence.Count > 0 && results.Count > 0) {
				// Cache the results, making sure to clone everything.
				this.LastSequence = new List<byte>(sequence);
				this.LastResults = new List<CommandDefinition>(results);
			}
		}

		/// <summary>
		/// Gets a cached set of cached command definitions based on the specified
		/// byte sequence, if the byte sequence is a subset of the previously queried
		/// byte sequence.
		/// </summary>
		/// <param name="sequence">The byte sequence to get cached results for.</param>
		/// <returns>A list of cached command definitions, or all command definitions if no suitable cache was available.</returns>
		private ICollection<CommandDefinition> GetCache(IList<byte> sequence) {
			if (sequence == null)
				throw new ArgumentNullException(nameof(sequence), "The byte sequence cannot be null.");

			LinkedList<CommandDefinition> defs = null;

			// Check if there is a cached sequence smaller than the current one.
			if (this.LastSequence != null && this.LastSequence.Count <= sequence.Count) {
				// Check if the cached sequence is a subset of the current one.
				bool isSuitable = true;
				for (int i = 0; i < this.LastSequence.Count; i++) {
					if (this.LastSequence[i] != sequence[i]) {
						isSuitable = false;
						break;
					}
				}

				// Load the cached results if applicable.
				if (isSuitable) {
					defs = new LinkedList<CommandDefinition>(this.LastResults);
				}
			}

			// If no suitable cache found, filter only on first byte.
			if (defs == null) {
				byte first = sequence[0];
				defs = new LinkedList<CommandDefinition>();
				foreach (CommandDefinition def in this.definitions) {
					if ((first & def.Mask[0]) == def.Base[0]) {
						defs.AddLast(def);
					}
				}
			}

			return defs;
		}

		/// <summary>
		/// Finds all commands that are a potential match for the given byte sequence.
		/// If the sequence is a subset of the previously used sequence, the cached
		/// results are used as a starting point.
		/// </summary>
		/// <param name="sequence">The byte sequence to match.</param>
		/// <returns>The matched commands.</returns>
		public IList<CommandDefinition> Match(IList<byte> sequence) {
			if (sequence == null)
				throw new ArgumentNullException(nameof(sequence), "The byte sequence cannot be null.");

			// Get a cached set of results, if available.
			ICollection<CommandDefinition> defs = GetCache(sequence);

			// Keep track of the maximum base length.
			int maxBaseLength = 0;

			// Check every byte in the sequence, while there are command definitions left.
			for (int i = 0; i < sequence.Count && defs.Count > 0; i++) {
				// Remove each command that does not match.
				foreach (CommandDefinition def in defs.ToArray()) {
					// Update the max base length.
					if (def.Base.Count > maxBaseLength) {
						maxBaseLength = def.Base.Count;
					}

					// Remove if base length exceeded or mismatch.
					if (sequence.Count > def.MinimumLength || (sequence[i] & def.Mask[i]) != def.Base[i]) {
						defs.Remove(def);
					}
				}
			}

			// If multiple commands were found, but all bases have been checked completely, return the command with the highest priority.
			//if (defs.Count > 1 && sequence.Count >= maxBaseLength) {
			//	CommandDefinition r = defs.First();
			//	if (r.PriorityLength <= 0) {
			//		defs = new List<CommandDefinition>(1);
			//		defs.Add(r);
			//		Console.WriteLine("WARNING: Full base priority is deprecated (used for \"" + r.Name + "\")");
			//		Console.ReadKey();
			//	}
			//}

			// Cache the results.
			Cache(sequence, defs);

			return new List<CommandDefinition>(defs);
		}

		/// <summary>
		/// Creates a new script command augmented from the specified command with the new specified command definition.
		/// </summary>
		/// <param name="cmd">The script command to augment.</param>
		/// <param name="extDef">The new definition for the command.</param>
		/// <returns>The augmented command.</returns>
		private static Command AugmentCommand(Command cmd, CommandDefinition newDef) {
			// Create a new command.
			Command newCmd = new Command(newDef);

			// Copy over the elements.
			foreach (CommandElement elem in cmd.Elements) {
				CommandElement newElem = newCmd.Elements[elem.Name];

				// Add the data entries.
				for (int i = 0; i < elem.Count; i++) {
					ReadOnlyNamedCollection<Parameter> dataEntry = newElem.CreateDataEntry();

					// Copy over the parameters.
					foreach (Parameter par in elem[i]) {
						dataEntry[par.Name].Bytes = par.Bytes;
					}

					newElem.Add(dataEntry);
				}
			}

			return newCmd;
		}

		/// <summary>
		/// Validates the specified command; if it is found to be invalid, creates a valid version of the command using a different command definition, if possible.
		/// </summary>
		/// <param name="cmd">The command to validate.</param>
		/// <returns>The valid command (possibly the original command), or null if no valid command could be created.</returns>
		public Command MakeValidCommand(Command cmd) {
			if (cmd == null)
				throw new ArgumentNullException(nameof(cmd), "The command to make valid cannot be null.");

			// Check if already suitable.
			if (IsSuitable(cmd, cmd.Definition)) {
				return cmd;
			}

			foreach (CommandDefinition canDef in this.Find(cmd.Name)) {
				if (IsSuitable(cmd, canDef)) {
					return AugmentCommand(cmd, canDef);
				}
			}

			return null;
		}

		/// <summary>
		/// Checks whether the specified command definition is suitable for the current state of the specified command.
		/// </summary>
		/// <param name="command">The command to check against.</param>
		/// <param name="newDefinition">The command definition to check for suitability.</param>
		/// <returns>true if the command definition is suitable; otherwise, false.</returns>
		private static bool IsSuitable(Command cmd, CommandDefinition newCmdDef) {
			// Loop through all elements.
			foreach (CommandElement elem in cmd.Elements) {
				// Check if the new definition contains all elements.
				if (!newCmdDef.Elements.Contains(elem.Name)) {
					return false;
				}
				CommandElementDefinition newElemDef = newCmdDef.Elements[elem.Name];

				// Loop through all data entries.
				foreach (ReadOnlyNamedCollection<Parameter> entry in elem) {
					foreach (Parameter par in entry) {
						// Check if the new definition contains all parameters.
						if (!newElemDef.DataParameterDefinitions.Contains(par.Name)) {
							return false;
						}
						ParameterDefinition newParDef = newElemDef.DataParameterDefinitions[par.Name];

						// Check if parameter is in range.
						if (!newParDef.InRange(par.ToInt64())) {
							return false;
						}
					}
				}
			}

			// All checks passed.
			return true;
		}

		/// <summary>
		/// Returns an enumerator that iterates through the script commands in this command database.
		/// </summary>
		/// <returns>The enumerator.</returns>
		IEnumerator IEnumerable.GetEnumerator() {
			return this.definitions.GetEnumerator();
		}

		/// <summary>
		/// Returns an enumerator that iterates through the script commands in this command database.
		/// </summary>
		/// <returns>The enumerator.</returns>
		public IEnumerator<CommandDefinition> GetEnumerator() {
			return this.definitions.GetEnumerator();
		}
	}
}
