﻿using LibTextPet.General;
using LibTextPet.Msg;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LibTextPet.IO.Msg {
	/// <summary>
	/// A reader that reads text archives from a ROM.
	/// </summary>
	public class ROMTextArchiveReader : ROMManager, IReader<TextArchive> {
		/// <summary>
		/// Gets the underlying text archive reader that is used to read text archives from the input stream.
		/// </summary>
		public BinaryTextArchiveReader TextArchiveReader { get; }

		/// <summary>
		/// Creates a new ROM text archive reader that reads from the specified input stream and uses the specified game info.
		/// </summary>
		/// <param name="stream">The stream to read from.</param>
		/// <param name="game">The game info.</param>
		public ROMTextArchiveReader(Stream stream, GameInfo game, ROMEntryCollection romEntries)
			: base(stream, FileAccess.Read, game, romEntries) {
			this.TextArchiveReader = new BinaryTextArchiveReader(stream, game);
		}

		/// <summary>
		/// Reads a text archive from the current input stream.
		/// </summary>
		/// <returns></returns>
		public TextArchive Read() {
			long start = this.BaseStream.Position;
			bool compressed = false;
			int size = 0;

			// Check if there is an entry for the current position already.
			bool entryExists = this.ROMEntries.Contains((long)this.BaseStream.Position);
			ROMEntry entry;

			if (entryExists) {
				entry = this.ROMEntries[start];

				// Check if there are enough bytes left for the entry.
				if (this.BaseStream.Position + entry.Size > this.BaseStream.Length) {
					throw new InvalidOperationException("The size of the current ROM entry exceeds the number of bytes left in the current input stream.");
				}
			} else {
				entry = new ROMEntry();
			}

			TextArchive ta = null;

			// Try compressed first.
			if (!entryExists || entry.Compressed) {
				this.BaseStream.Position = start;
				using (MemoryStream ms = LZ77.Decompress(this.BaseStream)) {
					if (ms != null && ms.Length > 4) {
						BinaryReader binReader = new BinaryReader(ms);
						int offset = 0;
						int length = (int)ms.Length;

						// Skip length header, if present.
						if (binReader.ReadByte() == 0 && (binReader.ReadUInt16() + (binReader.ReadByte() << 16)) == ms.Length) {
							offset = 4;
							length -= 4;
						}

						ms.Position = offset;
						ta = new BinaryTextArchiveReader(ms, this.Game).Read(length);

						if (entryExists && entry.Compressed && ta == null) {
							// ROM entry dictates that the text archive should be compressed, but no compressed text archive could be read.
							return null;
						}

						if (ta != null) {
							// Text archive was read successfully.
							compressed = true;
							// Calculate the compressed size.
							size = (int)(this.BaseStream.Position - start);
						}
					}
				}
			}

			// Try uncompressed.
			if (ta == null) {
				if (entryExists) {
					// Use the existing ROM entry.
					this.BaseStream.Position = start;
					ta = this.TextArchiveReader.Read(entry.Size);
					size = entry.Size;
				} else {
					// No ROM entry available; need to determine the size.
					this.BaseStream.Position = start;
					ta = this.TextArchiveReader.Read();

					if (ta != null) {
						// Get the size.
						size = (int)(this.BaseStream.Position - start);

						// Check if the text archive encroaches on any other known ROM entries.
						foreach (ROMEntry otherEntry in this.ROMEntries) {
							// Ignore any entry for a text archive at or before this entry.
							if (otherEntry.Offset <= entry.Offset) {
								continue;
							}

							if (this.BaseStream.Position > otherEntry.Offset) {
								// Clear the last script.
								ta[ta.Count - 1] = new Script(this.Databases[0].Name);

								// Subtract the size of the last script from the text archive size.
								size -= (int)(this.BaseStream.Position - this.TextArchiveReader.ScriptReader.StartPosition);
							}
						}
					}
				}
			}

			// Check if any script contains a script-ending command.
			if (ta != null) {
				bool found = false;
				bool outOfBoundsJump = false;
				int maxOverflow = 0;
				foreach (Script script in ta) {
					int currentOverflow = 0;
					bool ended = false;

					// Cycle through all elements in the script.
					foreach (IScriptElement elem in script) {
						// Keep track of the number of elements past the script-ending element.
						if (ended) {
							currentOverflow++;
						}

						Command cmd = elem as Command;
						if (cmd != null) {
							if (cmd.EndsScript) {
								ended = true;
							}
							if (cmd.Definition.EndType == EndType.Always) {
								found = true;
							}
							foreach (Parameter par in cmd.Parameters) {
								if (par.IsJump && par.ToInt64() != 0xFF && par.ToInt64() >= ta.Count) {
									outOfBoundsJump = true;
								}
							}
							foreach (IEnumerable<Parameter> dataEntry in cmd.Data) {
								foreach (Parameter par in dataEntry) {
									if (par.IsJump && par.ToInt64() != 0xFF && par.ToInt64() >= ta.Count) {
										outOfBoundsJump = true;
									}
								}
							}
						}
					}
					
					if (currentOverflow > maxOverflow) {
						maxOverflow = currentOverflow;
					}
				}

				// If the text archive contains no script-ending elements, it's probably not a text archive.
				if (!found) {
					ta = null;
				}

				// If the 'overflow' is too high, it's probably not a text archive.
				if (maxOverflow > 2) {
					ta = null;
				}

				// If there are out-of-bounds jumps, it's probably not a text archive.
				if (outOfBoundsJump) {
					ta = null;
				}
			}

			if (ta != null && !entryExists && this.UpdateROMEntriesAndIdentifiers) {
				// Create a new ROM entry.
				// TODO: Read the pointers
				entry = new ROMEntry((int)start, size, compressed, new int[0]);
			}

			// Set the identifier of the text archive if it could be read.
			if (ta != null) {
				ta.Identifier = entry.Offset.ToString("X6", CultureInfo.InvariantCulture);
			}

			return ta;
		}
	}
}
