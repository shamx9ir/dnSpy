﻿/*
    Copyright (C) 2014-2017 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using dnlib.DotNet;
using dnSpy.Contracts.Decompiler;
using dnSpy.Decompiler.Properties;

namespace dnSpy.Decompiler {
	static class TypeFormatterUtils {
		public const int DigitGroupSizeHex = 4;
		public const int DigitGroupSizeDecimal = 3;
		public const string DigitSeparator = "_";
		public const string NaN = "NaN";
		public const string NegativeInfinity = "-Infinity";
		public const string PositiveInfinity = "Infinity";

		public static string ToFormattedNumber(bool digitSeparators, string prefix, string number, int digitGroupSize) {
			if (digitSeparators)
				number = AddDigitSeparators(number, digitGroupSize, DigitSeparator);

			string res = number;
			if (prefix.Length != 0)
				res = prefix + res;
			return res;
		}

		static string AddDigitSeparators(string number, int digitGroupSize, string digitSeparator) {
			if (number.Length <= digitGroupSize)
				return number;

			var sb = new StringBuilder();

			for (int i = 0; i < number.Length; i++) {
				int d = number.Length - i;
				if (i != 0 && (d % digitGroupSize) == 0 && number[i - 1] != '-')
					sb.Append(DigitSeparator);
				sb.Append(number[i]);
			}

			return sb.ToString();
		}

		public const int MAX_RECURSION = 200;
		public const int MAX_OUTPUT_LEN = 1024 * 4;

		public static string FilterName(string s) {
			const int MAX_NAME_LEN = 0x100;
			if (s == null)
				return "<<NULL>>";

			var sb = new StringBuilder(s.Length);

			foreach (var c in s) {
				if (sb.Length >= MAX_NAME_LEN)
					break;
				if (c >= ' ')
					sb.Append(c);
				else
					sb.Append(string.Format("\\u{0:X4}", (ushort)c));
			}

			if (sb.Length > MAX_NAME_LEN)
				sb.Length = MAX_NAME_LEN;
			return sb.ToString();
		}

		public static string RemoveGenericTick(string s) {
			int index = s.LastIndexOf('`');
			if (index < 0)
				return s;
			if (s[0] == '<')	// check if compiler generated name
				return s;
			return s.Substring(0, index);
		}

		public static string GetFileName(string s) {
			// Don't use Path.GetFileName() since it can throw if input contains invalid chars
			int index = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
			if (index < 0)
				return s;
			return s.Substring(index + 1);
		}

		public static string GetNumberOfOverloadsString(TypeDef type, string name) {
			int overloads = TypeFormatterUtils.GetNumberOfOverloads(type, name);
			if (overloads == 1)
				return string.Format(" (+ {0})", dnSpy_Decompiler_Resources.ToolTip_OneMethodOverload);
			else if (overloads > 1)
				return string.Format(" (+ {0})", string.Format(dnSpy_Decompiler_Resources.ToolTip_NMethodOverloads, overloads));
			return null;
		}

		static int GetNumberOfOverloads(TypeDef type, string name) {
			var hash = new HashSet<MethodDef>(MethodEqualityComparer.DontCompareDeclaringTypes);
			while (type != null) {
				foreach (var m in type.Methods) {
					if (m.Name == name)
						hash.Add(m);
				}
				type = type.BaseType.ResolveTypeDef();
			}
			return hash.Count - 1;
		}

		public static string GetPropertyName(IMethod method) {
			if (method == null)
				return null;
			var name = method.Name;
			if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal))
				return name.Substring(4);
			return null;
		}

		public static string GetName(ISourceVariable variable) {
			var n = variable.Name;
			if (!string.IsNullOrWhiteSpace(n))
				return n;
			if (variable.Variable != null) {
				if (variable.IsLocal)
					return "V_" + variable.Variable.Index.ToString();
				return "A_" + variable.Variable.Index.ToString();
			}
			Debug.Fail("Decompiler generated variable without a name");
			return "???";
		}

		public static bool IsSystemNullable(GenericInstSig gis) {
			var gt = gis.GenericType as ValueTypeSig;
			return gt != null &&
				gt.TypeDefOrRef != null &&
				gt.TypeDefOrRef.DefinitionAssembly.IsCorLib() &&
				gt.TypeDefOrRef.FullName == "System.Nullable`1";
		}

		public static bool IsSystemValueTuple(GenericInstSig gis) => GetSystemValueTupleRank(gis) >= 0;

		static int GetSystemValueTupleRank(GenericInstSig gis) {
			int rank = 0;
			for (int i = 0; i < 1000; i++) {
				int currentRank = GetValueTupleSimpleRank(gis);
				if (currentRank < 0)
					return -1;
				if (rank < 8)
					return rank + currentRank;
				rank += currentRank - 1;
				gis = gis.GenericArguments[currentRank - 1] as GenericInstSig;
				if (gis == null)
					return -1;
			}
			return -1;
		}

		static int GetValueTupleSimpleRank(GenericInstSig gis) {
			var gt = gis.GenericType as ValueTypeSig;
			if (gt == null)
				return -1;
			if (gt.TypeDefOrRef == null)
				return -1;
			if (gt.Namespace != "System")
				return -1;
			int rank;
			switch (gt.TypeDefOrRef.Name.String) {
			case "ValueTuple`1": rank = 1; break;
			case "ValueTuple`2": rank = 2; break;
			case "ValueTuple`3": rank = 3; break;
			case "ValueTuple`4": rank = 4; break;
			case "ValueTuple`5": rank = 5; break;
			case "ValueTuple`6": rank = 6; break;
			case "ValueTuple`7": rank = 7; break;
			case "ValueTuple`8": rank = 8; break;
			default: return -1;
			}
			if (gis.GenericArguments.Count != rank)
				return -1;
			return rank;
		}

		public static bool IsDelegate(TypeDef td) => td != null &&
			new SigComparer().Equals(td.BaseType, td.Module.CorLibTypes.GetTypeRef("System", "MulticastDelegate")) &&
			td.BaseType.DefinitionAssembly.IsCorLib();

		public static (PropertyDef property, AccessorKind kind) TryGetProperty(MethodDef method) {
			if (method == null)
				return (null, AccessorKind.None);
			foreach (var p in method.DeclaringType.Properties) {
				if (method == p.GetMethod)
					return (p, AccessorKind.Getter);
				if (method == p.SetMethod)
					return (p, AccessorKind.Setter);
			}
			return (null, AccessorKind.None);
		}

		public static (EventDef @event, AccessorKind kind) TryGetEvent(MethodDef method) {
			if (method == null)
				return (null, AccessorKind.None);
			foreach (var e in method.DeclaringType.Events) {
				if (method == e.AddMethod)
					return (e, AccessorKind.Adder);
				if (method == e.RemoveMethod)
					return (e, AccessorKind.Remover);
			}
			return (null, AccessorKind.None);
		}

		public static bool IsDeprecated(IMethod method) {
			var md = method.ResolveMethodDef();
			if (md == null)
				return false;
			return IsDeprecated(md.CustomAttributes);
		}

		public static bool IsDeprecated(IField field) {
			var fd = field.ResolveFieldDef();
			if (fd == null)
				return false;
			return IsDeprecated(fd.CustomAttributes);
		}

		public static bool IsDeprecated(PropertyDef prop) {
			if (prop == null)
				return false;
			return IsDeprecated(prop.CustomAttributes);
		}

		public static bool IsDeprecated(EventDef evt) {
			if (evt == null)
				return false;
			return IsDeprecated(evt.CustomAttributes);
		}

		public static bool IsDeprecated(ITypeDefOrRef type) {
			var td = type.ResolveTypeDef();
			if (td == null)
				return false;
			return IsDeprecated(td.CustomAttributes);
		}

		static bool IsDeprecated(CustomAttributeCollection customAttributes) {
			foreach (var ca in customAttributes) {
				if (ca.TypeFullName == "System.ObsoleteAttribute")
					return true;
			}
			return false;
		}

		static bool IsExtension(CustomAttributeCollection customAttributes) {
			foreach (var ca in customAttributes) {
				if (ca.TypeFullName == "System.Runtime.CompilerServices.ExtensionAttribute")
					return true;
			}
			return false;
		}

		static bool IsAwaitableType(TypeSig type) {
			if (type == null)
				return false;

			var td = type.Resolve();
			if (td == null)
				return false;
			return IsAwaitableType(td.FullName);
		}

		static bool IsAwaitableType(string fullName) =>
			fullName == "System.Threading.Tasks.Task" ||
			fullName == "System.Threading.Tasks.Task`1" ||
			fullName == "System.Threading.Tasks.ValueTask`1";

		public static MemberSpecialFlags GetMemberSpecialFlags(IMethod method) {
			var flags = MemberSpecialFlags.None;

			var md = method.ResolveMethodDef();
			if (md != null && IsExtension(md.CustomAttributes))
				flags |= MemberSpecialFlags.Extension;

			if (IsAwaitableType(method.MethodSig.GetRetType()))
				flags |= MemberSpecialFlags.Awaitable;

			return flags;
		}

		public static MemberSpecialFlags GetMemberSpecialFlags(ITypeDefOrRef type) {
			var flags = MemberSpecialFlags.None;

			if (IsAwaitableType(type.FullName))
				flags |= MemberSpecialFlags.Awaitable;

			return flags;
		}

		public static bool IsDefaultParameter(ParamDef pd) {
			if (pd == null)
				return false;
			if (pd.Constant != null)
				return true;
			foreach (var ca in pd.CustomAttributes) {
				var type = ca.AttributeType;
				while (type != null) {
					var fullName = type.FullName;
					if (fullName == "System.Runtime.CompilerServices.CustomConstantAttribute" ||
						fullName == "System.Runtime.CompilerServices.DecimalConstantAttribute")
						return true;
					type = type.GetBaseType();
				}
			}
			return false;
		}
	}

	enum AccessorKind {
		None,
		Getter,
		Setter,
		Adder,
		Remover,
	}

	[Flags]
	enum MemberSpecialFlags {
		None = 0,
		Extension = 1,
		Awaitable = 2,
	}
}
