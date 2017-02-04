﻿using LibTextPet.General;
using LibTextPet.Msg;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LibTextPet.IO.Msg {
	/// <summary>
	/// A reader that reads text archives from a file.
	/// </summary>
	public class FileTextArchiveReader : FileManager, IReader<TextArchive> {
		/// <summary>
		/// Gets the underlying text archive reader that is used to read text archives from the input stream.
		/// </summary>
		public BinaryTextArchiveReader TextArchiveReader { get; }

		/// <summary>
		/// Gets or sets a boolean that indicates whether this file text archive reader should verify that any text archive read is 'good', and discard it if it is not.
		/// </summary>
		public bool CheckGoodTextArchive { get; set; }

		/// <summary>
		/// Gets or sets a boolean that indicates whether this file text archive reader should attempt to read the entire (rest of the) file.
		/// </summary>
		public bool ReadEntireFile { get; set; }

		/// <summary>
		/// Gets or sets the minimum size, in bytes, for all text archives; any archives smaller than this will be discarded if they have to pointers.
		/// </summary>
		public int MinimumSize { get; set; }

		/// <summary>
		/// Gets or sets a boolean that indicates whether this file text archive reader should find the pointers of all text archives that were successfully read.
		/// </summary>
		public bool SearchPointers { get; set; }

		/// <summary>
		/// Creates a new file text archive reader that reads from the specified input stream and uses the specified game info.
		/// </summary>
		/// <param name="stream">The stream to read from.</param>
		/// <param name="game">The game info.</param>
		public FileTextArchiveReader(Stream stream, GameInfo game)
			: this(stream, game, new FileIndexEntryCollection()) { }

		/// <summary>
		/// Creates a new file text archive reader that reads from the specified input stream and uses the specified game info and file entries.
		/// </summary>
		/// <param name="stream">The stream to read from.</param>
		/// <param name="game">The game info.</param>
		/// <param name="fileIndex">The file entries to use.</param>
		public FileTextArchiveReader(Stream stream, GameInfo game, FileIndexEntryCollection fileIndex)
			: base(stream, FileAccess.Read, game, fileIndex) {
			this.TextArchiveReader = new BinaryTextArchiveReader(stream, game);
			this.CheckGoodTextArchive = false;
			this.SearchPointers = true;
			this.ReadEntireFile = false;
			this.MinimumSize = 0;
		}

		/// <summary>
		/// Reads a text archive from the current input stream.
		/// </summary>
		/// <returns></returns>
		public TextArchive Read() {
			long start = this.BaseStream.Position;
			bool compressed = false;
			bool sizeHeader = false;
			int size = 0;

			// Check if there is an entry for the current position already.
			bool entryExists = this.FileIndex.Contains((long)this.BaseStream.Position);
			FileIndexEntry entry;

			if (entryExists) {
				entry = this.FileIndex[start];

				// Check if there are enough bytes left for the entry.
				if (this.BaseStream.Position + entry.Size > this.BaseStream.Length) {
					throw new InvalidOperationException("The size of the current file index entry exceeds the number of bytes left in the current input stream.");
				}
			} else {
				entry = new FileIndexEntry((int)start);
			}

			TextArchive ta = null;

			// Try compressed first.
			if (!entryExists || entry.Compressed) {
				this.BaseStream.Position = start;
				using (MemoryStream ms = new MemoryStream()) {
					if (LZ77.Decompress(this.BaseStream, ms) && ms.Length > 4) {
						ms.Position = 0;
						BinaryReader binReader = new BinaryReader(ms);
						int offset = 0;
						int length = (int)ms.Length;

						// Skip length header, if present.
						if (length >= 4) {
							long header = binReader.ReadUInt32();
							if ((header & 0xFF) == 0 && ((header >> 8) == length || (header >> 8) == length - 4)) {
								offset = 4;
								length -= 4;
								// TODO: BN5DS uses size - 4, mark this somehow.
								sizeHeader = true;
							}
						}

						ms.Position = offset;
						BinaryTextArchiveReader tempReader = new BinaryTextArchiveReader(ms, this.Game);
						tempReader.IgnorePointerSyncErrors = this.TextArchiveReader.IgnorePointerSyncErrors;
						tempReader.AutoSortPointers = this.TextArchiveReader.AutoSortPointers;
						ta = tempReader.Read(length);

						if (entryExists && entry.Compressed && ta == null) {
							// File index entry dictates that the text archive should be compressed, but no compressed text archive could be read.
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
				this.BaseStream.Position = start;
				if (this.ReadEntireFile) {
					// Read the rest of the file.
					size = (int)(this.BaseStream.Length - this.BaseStream.Position);
					ta = this.TextArchiveReader.Read(size);
				} else if (entryExists && entry.Size > 0) {
					// Use the existing file index entry.
					size = entry.Size;
					ta = this.TextArchiveReader.Read(size);
				} else {
					// No file index entry or size available; need to determine the size.
					ta = this.TextArchiveReader.Read();
					size = (int)(this.BaseStream.Position - start);

					if (ta != null && CheckOverlap(start, size)) {
						// Clear the last script.
						ta[ta.Count - 1] = new Script(this.Databases[0].Name);

						// Subtract the size of the last script from the text archive size.
						size -= (int)(this.BaseStream.Position - this.TextArchiveReader.ScriptReader.StartPosition);
					}
				}
			}

			if (ta == null) {
				// Could not read text archive.
				return null;
			}

			// Check if any script contains a script-ending command.
			if (this.CheckGoodTextArchive && !IsGoodTextArchive(ta)) {
				return null;
			}

			// Search for pointers.
			IEnumerable<int> pointers = new int[0];
			if (this.SearchPointers) {
				pointers = FindPointers((int)start);
			}

			// Check the size of the text archive.
			if (size < MinimumSize && !pointers.Any()) {
				return null;
			}

			// Update the file index entry.
			if (this.UpdateFileIndex) {
				UpdateEntry(start, compressed, sizeHeader, size, entry, pointers);
			}

			// Set the identifier of the text archive if it could be read.
			ta.Identifier = start.ToString("X6", CultureInfo.InvariantCulture);

			return ta;
		}

		private void UpdateEntry(long start, bool compressed, bool sizeHeader, int size, FileIndexEntry entry, IEnumerable<int> pointers) {
			if (this.FileIndex.Contains(entry)) {
				// Update the size and compression flags.
				this.FileIndex[start].Size = size;
				this.FileIndex[start].Compressed = compressed;
				this.FileIndex[start].SizeHeader = sizeHeader;
			} else {
				// Create a new file index entry.
				entry = new FileIndexEntry((int)start, size, compressed, sizeHeader, pointers);
				this.FileIndex.Add(entry);
			}
		}

		/// <summary>
		/// Find all pointers to the specified file offset in the input stream.
		/// </summary>
		/// <param name="offset">The file offset.</param>
		/// <returns>The offsets of all pointers that were found.</returns>
		private IEnumerable<int> FindPointers(int offset) {
			List<int> pointers = new List<int>();

			int read;
			uint value;
			byte[] buffer = new byte[4];

			for (int i = 0; i < (int)this.BaseStream.Length; i++) {
				this.BaseStream.Position = i;
				read = this.BaseStream.Read(buffer, 0, buffer.Length);

				if (read >= buffer.Length) {
					value = BitConverter.ToUInt32(buffer, 0);

					if ((value & 0x7FFFFFFF) == (0x08000000 | offset)) {
						pointers.Add(i);
					}
				}
			}

			return pointers;
		}

		/// <summary>
		/// Checks if the specified memory block would overlap any of the currently loaded file index entries.
		/// </summary>
		/// <param name="offset">The offset of the memory block.</param>
		/// <param name="size">The size, in bytes, of the memory block.</param>
		/// <returns>true if there is overlap; otherwise, false;</returns>
		private bool CheckOverlap(long offset, int size) {
			// Check if the text archive encroaches on any other known file index entries.
			foreach (FileIndexEntry otherEntry in this.FileIndex) {
				// Ignore any entry for a text archive at or before this entry.
				if (otherEntry.Offset <= offset) {
					continue;
				}

				if (offset + size > otherEntry.Offset) {
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Checks if the specified text archive is 'good' -- likely to be a valid text archive.
		/// </summary>
		/// <param name="ta">The text archive to check.</param>
		/// <returns>true if the text archive is good; otherwise, false.</returns>
		private static bool IsGoodTextArchive(TextArchive ta) {
			if (ta == null) {
				return false;
			}
			
			// Check all scripts in the text archive.
			bool endTypeAlwaysFound = false;
			for (int i = 0; i < ta.Count; i++) {
				Script script = ta[i];
				int currentOverflow = 0;
				bool scriptEnded = false;

				// Cycle through all elements in the script.
				foreach (IScriptElement elem in script) {
					// Keep track of the number of elements past the script-ending element.
					if (scriptEnded) {
						currentOverflow++;
					}

					// Check if the element is a command.
					Command cmd = elem as Command;
					if (cmd == null) {
						continue;
					}

					// Check the end type of the command, and update flags accordingly.
					if (cmd.EndsScript) {
						scriptEnded = true;

						if (cmd.Definition.EndType == EndType.Always) {
							endTypeAlwaysFound = true;
						}
					}						

					// If there are out-of-bounds jumps, it's probably not a text archive.
					// Don't count this on the last script (unless it's the only one).
					if ((i == ta.Count - 1 && i != 0) && CommandContainsOutOfRangeJump(cmd, ta.Count)) {
						return false;
					}
				}

				// If the 'overflow' is too high, it's probably not a text archive.
				if (currentOverflow > 3) {
					return false;
				}
			}

			// If the text archive contains no elements that always end the script, it's probably not a text archive.
			if (!endTypeAlwaysFound) {
				return false;
			}

			return true;
		}

		/// <summary>
		/// Checks if the specified command contains a jump parameter of which the value falls outside the range allowed by the given text archive size.
		/// </summary>
		/// <param name="cmd">The command to check.</param>
		/// <param name="taSize">The text archive size.</param>
		/// <returns>true if the command contains an out-of-range jump; otherwise, false.</returns>
		private static bool CommandContainsOutOfRangeJump(Command cmd, int taSize) {
			foreach (Parameter par in cmd.Parameters) {
				if (par.IsJump && par.ToInt64() != 0xFF && par.ToInt64() >= taSize) {
					return true;
				}
			}
			foreach (IEnumerable<Parameter> dataEntry in cmd.Data) {
				foreach (Parameter par in dataEntry) {
					if (par.IsJump && par.ToInt64() != 0xFF && par.ToInt64() >= taSize) {
						return true;
					}
				}
			}

			return false;
		}
	}
}
