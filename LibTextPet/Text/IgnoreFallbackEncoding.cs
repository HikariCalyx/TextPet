﻿using LibTextPet.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LibTextPet.Text {
	/// <summary>
	/// An encoding that ignores encoding and decoding errors and records them.
	/// </summary>
	public class IgnoreFallbackEncoding : Encoding, IPlugin {
		public string PluginType => "Encoding";

		private new EncoderIgnoreFallback EncoderFallback { get; }
		private new DecoderIgnoreFallback DecoderFallback { get; }

		private Encoding BaseEncoding { get; }

		public string Name
			=> this.BaseEncoding.EncodingName;

		public override string EncodingName
			=> this.BaseEncoding.EncodingName;

		/// <summary>
		/// Gets the amount of fallbacks that have occurred.
		/// </summary>
		public int FallbackCount
			=> this.EncoderFallback.FallbackCount + this.DecoderFallback.FallbackCount;


		/// <summary>
		/// Creates a new fallback-ignoring encoding based on the specified base encoding.
		/// </summary>
		/// <param name="baseEncoding">The base encoding.</param>
		public IgnoreFallbackEncoding(Encoding baseEncoding) {
			this.EncoderFallback = new EncoderIgnoreFallback();
			this.DecoderFallback = new DecoderIgnoreFallback();

			this.BaseEncoding = (Encoding)baseEncoding.Clone();
			this.BaseEncoding.EncoderFallback = this.EncoderFallback;
			this.BaseEncoding.DecoderFallback = this.DecoderFallback;
		}

		/// <summary>
		/// Resets the amount of fallbacks that have occurred.
		/// </summary>
		public void ResetFallbackCount() {
			this.EncoderFallback.ResetFallbackCount();
			this.DecoderFallback.ResetFallbackCount();
		}

		public override int GetByteCount(char[] chars, int index, int count)
			=> this.BaseEncoding.GetByteCount(chars, index, count);

		public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
			=> this.BaseEncoding.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

		public override int GetCharCount(byte[] bytes, int index, int count)
			=> this.BaseEncoding.GetCharCount(bytes, index, count);

		public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
			=> this.BaseEncoding.GetChars(bytes, byteIndex, byteCount, chars, charIndex);

		public override int GetMaxByteCount(int charCount)
			=> this.BaseEncoding.GetMaxByteCount(charCount);

		public override int GetMaxCharCount(int byteCount)
			=> this.BaseEncoding.GetMaxCharCount(byteCount);
	}
}
