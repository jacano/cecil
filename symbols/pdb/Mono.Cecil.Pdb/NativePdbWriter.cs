//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;

using Mono.Cecil.Cil;
using Mono.Collections.Generic;

#if !READ_ONLY

namespace Mono.Cecil.Pdb {

	public class NativePdbWriter : Cil.ISymbolWriter {

		readonly ModuleDefinition module;
		readonly SymWriter writer;
		readonly Dictionary<string, SymDocumentWriter> documents;
        readonly Func<string, string> sourcePathRewriter;
        readonly Action<Guid> guidProvider;
        private Guid guid;
        private int age;

        internal NativePdbWriter (ModuleDefinition module, SymWriter writer, Func<string, string> sourcePathRewriter = null, Action<Guid> guidProvider = null)
		{
			this.module = module;
			this.writer = writer;
			this.documents = new Dictionary<string, SymDocumentWriter> ();
            this.sourcePathRewriter = sourcePathRewriter;
            this.guidProvider = guidProvider;
        }

		public bool GetDebugHeader (out ImageDebugDirectory directory, out byte [] header)
		{
			header = writer.GetDebugInfo (out directory);

            if (directory.Type != 2) //IMAGE_DEBUG_TYPE_CODEVIEW
                return false;
            if (directory.MajorVersion != 0 || directory.MinorVersion != 0)
                return false;

            if (header.Length < 24)
                return false;

            var magic = ReadInt32(header, 0);
            if (magic != 0x53445352)
                return false;

            var guid_bytes = new byte[16];
            Buffer.BlockCopy(header, 4, guid_bytes, 0, 16);

            this.guid = new Guid(guid_bytes);

            this.guidProvider?.Invoke(guid);

            this.age = ReadInt32(header, 20);

            return true;
		}

        static int ReadInt32(byte[] bytes, int start)
        {
            return (bytes[start]
                | (bytes[start + 1] << 8)
                | (bytes[start + 2] << 16)
                | (bytes[start + 3] << 24));
        }

        public void Write (MethodDebugInformation info)
		{
			var method_token = info.method.MetadataToken;
			var sym_token = new SymbolToken (method_token.ToInt32 ());

			writer.OpenMethod (sym_token);

			if (!info.sequence_points.IsNullOrEmpty ())
				DefineSequencePoints (info.sequence_points);

			if (info.scope != null)
				DefineScope (info.scope, info);

			writer.CloseMethod ();
		}

		void DefineScope (ScopeDebugInformation scope, MethodDebugInformation info)
		{
			var start_offset = scope.Start.Offset;
			var end_offset = scope.End.IsEndOfMethod
				? info.code_size
				: scope.End.Offset;

			writer.OpenScope (start_offset);

			var sym_token = new SymbolToken (info.local_var_token.ToInt32 ());

			if (!scope.variables.IsNullOrEmpty ()) {
				for (int i = 0; i < scope.variables.Count; i++) {
					var variable = scope.variables [i];
					CreateLocalVariable (variable, sym_token, start_offset, end_offset);
				}
			}

			if (!scope.scopes.IsNullOrEmpty ()) {
				for (int i = 0; i < scope.scopes.Count; i++)
					DefineScope (scope.scopes [i], info);
			}

			writer.CloseScope (end_offset);
		}

		void DefineSequencePoints (Collection<SequencePoint> sequence_points)
		{
			for (int i = 0; i < sequence_points.Count; i++) {
				var sequence_point = sequence_points [i];

				writer.DefineSequencePoints (
					GetDocument (sequence_point.Document),
					new [] { sequence_point.Offset },
					new [] { sequence_point.StartLine },
					new [] { sequence_point.StartColumn },
					new [] { sequence_point.EndLine },
					new [] { sequence_point.EndColumn });
			}
		}

		void CreateLocalVariable (VariableDebugInformation variable, SymbolToken local_var_token, int start_offset, int end_offset)
		{
			writer.DefineLocalVariable2 (
				variable.Name,
				variable.Attributes,
				local_var_token,
				SymAddressKind.ILOffset,
				variable.Index,
				0,
				0,
				start_offset,
				end_offset);
		}

		SymDocumentWriter GetDocument (Document document)
		{
			if (document == null)
				return null;

			SymDocumentWriter doc_writer;
			if (documents.TryGetValue (document.Url, out doc_writer))
				return doc_writer;

            var url = document.Url;
            if (sourcePathRewriter != null)
            {
                url = sourcePathRewriter(url);
            }

            doc_writer = writer.DefineDocument (
                url,
				document.Language.ToGuid (),
				document.LanguageVendor.ToGuid (),
				document.Type.ToGuid ());

			documents [document.Url] = doc_writer;
			return doc_writer;
		}

		public void Dispose ()
		{
			var entry_point = module.EntryPoint;
			if (entry_point != null)
				writer.SetUserEntryPoint (new SymbolToken (entry_point.MetadataToken.ToInt32 ()));

			writer.Close ();
		}
	}
}

#endif
