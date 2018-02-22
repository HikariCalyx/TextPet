﻿using LibTextPet.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LibTextPet.Text2 {
	/// <summary>
	/// Represents an encoding that can map a single byte to multiple characters.
	/// </summary>
	public class LookupTableEncoding2 : Encoding {
		private LookupTree<byte, string> BytesToStringLookup { get; }
		private LookupTree<char, byte[]> StringToBytesLookup { get; }

		private LookupTableDecoder _commonDecoder;
		internal LookupTableDecoder CommonDecoder {
			get {
				// Only create the decoder one time, since we always flush it anyway.
				if (this._commonDecoder == null) {
					this._commonDecoder = (LookupTableDecoder)this.GetDecoder();
				}
				return this._commonDecoder;
			}
		}

		public int MaxBytesPerCodePoint => this.BytesToStringLookup.Height;
		public int MaxCharsPerCodePoint => this.StringToBytesLookup.Height;

		public override Decoder GetDecoder() {
			return new LookupTableDecoder(this.BytesToStringLookup) {
				Fallback = this.DecoderFallback,
			};
		}

		public override int GetByteCount(char[] chars, int index, int count) {
			throw new NotImplementedException();
		}

		public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) {
			throw new NotImplementedException();
		}

		public override int GetCharCount(byte[] bytes, int index, int count) {
			// Use the decoder.
			Decoder decoder;
			if (this.IsReadOnly) {
				decoder = this.CommonDecoder;
			} else {
				// This might be kinda slow???
				decoder = this.GetDecoder();
			}
			return decoder.GetCharCount(bytes, index, count, true);
		}

		public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) {
			// Use the decoder.
			Decoder decoder;
			if (this.IsReadOnly) {
				decoder = this.CommonDecoder;
			} else {
				// This might be kinda slow???
				decoder = this.GetDecoder();
			}
			return decoder.GetChars(bytes, byteIndex, byteCount, chars, charIndex, true);
		}

		public override int GetMaxByteCount(int charCount) {
			return charCount * this.MaxBytesPerCodePoint;
		}

		public override int GetMaxCharCount(int byteCount) {
			return byteCount * this.MaxCharsPerCodePoint;
		}

		internal class LookupTableDecoder : Decoder {
			private LookupTree<byte, string> BytesToStringLookup { get; }

			private List<byte> Queue { get; }
			private int QueueIndex { get; set; }
			private int CodePointLength { get; set; }
			private string CodePointString { get; set; }

			public bool Greedy { get; set; }

			public LookupTableDecoder(LookupTree<byte, string> bytesToStringLookup) {
				this.BytesToStringLookup = bytesToStringLookup;

				// Initialize the queue.
				this.Queue = new List<byte>(bytesToStringLookup.Height);
				this.QueueIndex = 0;
				this.CodePointLength = 0;
				this.CodePointString = "";

				this.Greedy = false;
			}
			
			public override int GetCharCount(byte[] bytes, int index, int count) {
				return this.GetCharCount(bytes, index, count, false);
			}
			
			public override int GetCharCount(byte[] bytes, int index, int count, bool flush) {
				if (bytes == null)
					throw new ArgumentNullException(nameof(bytes), "The byte array cannot be null.");
				if (index < 0 || index >= bytes.Length)
					throw new ArgumentOutOfRangeException(nameof(index), index, "Index is out of range.");
				if (count < 0 || index + count > bytes.Length)
					throw new ArgumentOutOfRangeException(nameof(count), count, "Count is out of range.");

				// Process every byte in the byte array.
				for (int i = 0; i < count; i++) {
					this.Queue.Add(bytes[index + i]);
				}
				return this.ProcessQueue(flush, false, null);
			}

			public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) {
				return this.GetChars(bytes, byteIndex, byteCount, chars, charIndex, false);
			}

			public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, bool flush) {
				if (bytes == null)
					throw new ArgumentNullException(nameof(bytes), "The byte array cannot be null.");
				if (chars == null)
					throw new ArgumentNullException(nameof(chars), "The character array cannot be null.");
				if (byteIndex < 0 || byteIndex >= bytes.Length)
					throw new ArgumentOutOfRangeException(nameof(byteIndex), byteIndex, "Byte index is out of range.");
				if (byteCount < 0 || byteIndex + byteCount > bytes.Length)
					throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count is out of range.");
				if (charIndex < 0)
					throw new ArgumentOutOfRangeException(nameof(charIndex), charIndex, "Character index cannot be negative.");

				StringBuilder builder = new StringBuilder();

				// Process every byte in the byte array.
				for (int i = 0; i < byteCount; i++) {
					this.Queue.Add(bytes[byteIndex + i]);
				}
				int charCount = this.ProcessQueue(flush, true, builder);

				foreach (char c in builder.ToString()) {
					chars[charIndex + charCount] = c;
					charCount++;
				}

				return charCount;
			}

			/// <summary>
			/// Processes the current queue.
			/// </summary>
			/// <param name="flush">If true, the queue will be flushed by forcing fallbacks and repeating until it is empty.</param>
			/// <param name="update">If true, updates the state of the decoder.</param>
			/// <param name="builder">If set, the StringBuilder to append the (partially) decoded string to; if null, not used.</param>
			/// <returns>The amount of characters (partially) decoded from the string.</returns>
			private int ProcessQueue(bool flush, bool update, StringBuilder builder) {
				int charCount = 0;

				// Save the state of the decoder.
				byte[] prevQueue = null;
				int prevQueueIndex = this.QueueIndex;
				int prevCodeLen = this.CodePointLength;
				string prevCodeStr = null;
				if (!update) {
					prevQueue = new byte[this.Queue.Count];
					this.Queue.CopyTo(prevQueue);
					prevCodeStr = this.CodePointString;
				}

				// If flushing, keep going until the queue is empty.
				while (flush && this.Queue.Count > 0) {
					// Process new bytes in the current queue.
					for (int i = this.QueueIndex; i < this.Queue.Count; i++) {
						// If this is the first byte, clear the current code point.
						if (i == 0) {
							this.CodePointLength = 0;
						}

						// Step on next byte in queue.
						if (this.BytesToStringLookup.Step(this.Queue[i])) {
							// Check if we reached a value.
							if (this.BytesToStringLookup.AtValue) {
								// Get the code point.
								this.CodePointString = this.BytesToStringLookup.CurrentValue;
								this.CodePointLength = i;
								if (this.Greedy || this.BytesToStringLookup.AtEnd) {
									// If greedy or dead end, use the first code point found.
									doCodePoint();
								} else {
									// Otherwise, keep looking.
									continue;
								}
							} else {
								// Did not reach value, but queue is still valid.
								continue;
							}
						} else if (this.CodePointLength > 0) {
							// Could not step on this byte. Use the last read code point.
							doCodePoint();
						} else {
							// Could not step on this byte and no valid code point read.
							doFallback();
						}
						
						// Reset to start of queue.
						i = -1;
					}

					// At this point, all bytes in queue processed.
					// If there are bytes left in the queue, these are trailing.
					// Continue from current position on next call.
					this.QueueIndex = this.Queue.Count;

					// Trailing bytes that we cannot match.
					// If flushing, have to fallback on the first byte to get rid of it.
					if (flush) {
						// Fallback on first byte.
						doFallback();
						this.QueueIndex = 0;
					}
				}

				// Restore decoder state.
				if (!update) {
					this.Queue.Clear();
					this.Queue.AddRange(prevQueue);
					this.QueueIndex = prevQueueIndex;
					this.CodePointLength = prevCodeLen;
					this.CodePointString = prevCodeStr;
				}

				return charCount;

				void doCodePoint() {
					if (builder != null) {
						builder.Append(this.CodePointString);
					}
					charCount += this.CodePointString.Length;
					// Remove bytes from queue.
					this.Queue.RemoveRange(0, this.CodePointLength);
					// Start next code point.
					this.BytesToStringLookup.Reset();
				}
				void doFallback() {
					byte[] fallbackBytes = new byte[] { this.Queue[0] };
					if (this.FallbackBuffer.Fallback(fallbackBytes, 0)) {
						// Append all fallback characters.
						while (this.FallbackBuffer.Remaining > 0) {
							if (builder != null) {
								builder.Append(this.FallbackBuffer.GetNextChar());
							}
							charCount += 1;
						}
					} else {
						byte[] queueArr = this.Queue.ToArray();
						throw new DecoderFallbackException("Could not decode " + BitConverter.ToString(queueArr) + ".", queueArr, 0);
					}
					this.FallbackBuffer.Reset();
					// Remove byte from queue.
					this.Queue.RemoveAt(0);
					// Start next code point.
					this.BytesToStringLookup.Reset();
				}
			}
		}
	}
}
