/*
    Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

    All rights reserved.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2CppInspector.Reflection
{
    // See: https://docs.microsoft.com/en-us/dotnet/api/system.reflection.customattributedata?view=netframework-4.8
    public class CustomAttributeData
    {
        // IL2CPP-specific data
        public TypeModel Model => AttributeType.Assembly.Model;
        public int Index { get; set; }

        // The type of the attribute
        public TypeInfo AttributeType { get; set; }

        public (ulong Start, ulong End) VirtualAddress =>
            // The last one will be wrong but there is no way to calculate it
            (Model.Package.CustomAttributeGenerators[Index], Model.Package.FunctionAddresses[Model.Package.CustomAttributeGenerators[Index]]);

        // C++ method names
        // TODO: Known issue here where we should be using CppDeclarationGenerator.TypeNamer to ensure uniqueness
        public string Name => $"{AttributeType.Name.ToCIdentifier()}_CustomAttributesCacheGenerator";

        // C++ method signature
        public string Signature => $"void {Name}(CustomAttributesCache *)";

        public override string ToString() => "[" + AttributeType.FullName + "]";

        // Get the machine code of the C++ function
        public byte[] GetMethodBody() => Model.Package.BinaryImage.ReadMappedBytes(VirtualAddress.Start, (int) (VirtualAddress.End - VirtualAddress.Start));

        // Get all the custom attributes for a given assembly, type, member or parameter
        private static IEnumerable<CustomAttributeData> getCustomAttributesYep(Assembly asm, int customAttributeIndex, uint token) {
            var attributeIndex = asm.Model.GetCustomAttributeIndex(asm, token, customAttributeIndex);
            if (attributeIndex <= 0)
                yield break;

            var pkg = asm.Model.Package;

            // Attribute type ranges weren't included before v21 (customASttributeGenerators was though)
            if (pkg.Version < 21)
                yield break;

            if (pkg.Version >= 29) {
                var startRange = pkg.AttributeDataRanges[attributeIndex];
                var endRange = pkg.AttributeDataRanges[attributeIndex + 1];

                pkg.Metadata.Position = pkg.Metadata.Header.attributeDataOffset + startRange.startOffset;
                var buff = pkg.Metadata.ReadBytes((int) (endRange.startOffset - startRange.startOffset));

                CustomAttributeDataReader reader = null;
                try {
                    reader = new CustomAttributeDataReader(asm, pkg, buff);
                }
                catch {
                }

                if (reader == null || reader.Count == 0) {
                    yield break;
                }

                for (var i = 0; i < reader.Count; i++) {
                    var type = reader.GetCustomAttributes();
                    if (type == null) {
                        continue;
                    }

                    var attribute = new CustomAttributeData { Index = attributeIndex, AttributeType = type };
                    yield return attribute;
                }
            }
            else {
                var range = pkg.AttributeTypeRanges[attributeIndex];
                for (var i = range.start; i < range.start + range.count; i++) {
                    var typeIndex = pkg.AttributeTypeIndices[i];

                    if (asm.Model.AttributesByIndices.TryGetValue(i, out var attribute)) {
                        yield return attribute;
                        continue;
                    }

                    attribute = new CustomAttributeData { Index = attributeIndex, AttributeType = asm.Model.TypesByReferenceIndex[typeIndex] };

                    asm.Model.AttributesByIndices.TryAdd(i, attribute);
                    yield return attribute;
                }
            }
        }

        private static IList<CustomAttributeData> getCustomAttributes(Assembly asm, uint token, int customAttributeIndex) =>
                getCustomAttributesYep(asm, customAttributeIndex, token).ToList();

        public static IList<CustomAttributeData> GetCustomAttributes(Assembly asm) => getCustomAttributes(asm, asm.MetadataToken, asm.AssemblyDefinition.customAttributeIndex);
        public static IList<CustomAttributeData> GetCustomAttributes(EventInfo evt) => getCustomAttributes(evt.Assembly, evt.MetadataToken, evt.Definition.customAttributeIndex);
        public static IList<CustomAttributeData> GetCustomAttributes(FieldInfo field) => getCustomAttributes(field.Assembly, field.MetadataToken, field.Definition.customAttributeIndex);
        public static IList<CustomAttributeData> GetCustomAttributes(MethodBase method) => getCustomAttributes(method.Assembly, method.MetadataToken, method.Definition.customAttributeIndex);
        public static IList<CustomAttributeData> GetCustomAttributes(ParameterInfo param) => getCustomAttributes(param.DeclaringMethod.Assembly, param.MetadataToken, param.Definition.customAttributeIndex);
        public static IList<CustomAttributeData> GetCustomAttributes(PropertyInfo prop)
            => prop.Definition != null ? getCustomAttributes(prop.Assembly, prop.MetadataToken, prop.Definition.customAttributeIndex) : new List<CustomAttributeData>();
        public static IList<CustomAttributeData> GetCustomAttributes(TypeInfo type) => type.Definition != null? getCustomAttributes(type.Assembly, type.MetadataToken, type.Definition.customAttributeIndex) : new List<CustomAttributeData>();
    }

    public class CustomAttributeDataReader : BinaryReader {
        private readonly Il2CppInspector Inspector;
        private readonly Assembly Assembly;
        public long ctorBuffer;
        public long dataBuffer;

        public uint Count { get; set; }

        public CustomAttributeDataReader(Assembly assembly, Il2CppInspector inspector, byte[] buff) : base(new MemoryStream(buff)) {
            Assembly = assembly;
            Inspector = inspector;
            Count = this.ReadCompressedUInt32();
            ctorBuffer = BaseStream.Position;
            dataBuffer = BaseStream.Position + Count * 4;
        }

        public TypeInfo GetCustomAttributes() {
            try {
                BaseStream.Position = ctorBuffer;
                var ctorIndex = ReadInt32();
                var methodDef = Inspector.Metadata.Methods[ctorIndex];
                var typeDef = Inspector.Metadata.Types[methodDef.declaringType];
                ctorBuffer = BaseStream.Position;
                return Assembly.Model.TypesByDefinitionIndex[methodDef.declaringType];
            }
            catch {
                return null;
            }

            /*ctorBuffer = BaseStream.Position;

            BaseStream.Position = dataBuffer;
            var argumentCount = this.ReadCompressedUInt32();
            var fieldCount = this.ReadCompressedUInt32();
            var propertyCount = this.ReadCompressedUInt32();*/



            Debugger.Break();

            /*var customTypesList = new List<object>();

            for (var i = 0; i < argumentCount; i++) {
                var type = ReadAttributeType();
            }

            for (var i = 0; i < fieldCount; i++)
            {
                var str = AttributeDataToString(ReadAttributeDataValue());
                (var declaring, var fieldIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(typeDef);
                var fieldDef = metadata.fieldDefs[declaring.fieldStart + fieldIndex];
                argList.Add($"{metadata.GetStringFromIndex(fieldDef.nameIndex)} = {str}");
            }
            for (var i = 0; i < propertyCount; i++)
            {
                var str = AttributeDataToString(ReadAttributeDataValue());
                (var declaring, var propertyIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(typeDef);
                var propertyDef = metadata.propertyDefs[declaring.propertyStart + propertyIndex];
                argList.Add($"{metadata.GetStringFromIndex(propertyDef.nameIndex)} = {str}");
            }
            dataBuffer = BaseStream.Position;


            var typeName = metadata.GetStringFromIndex(typeDef.nameIndex).Replace("Attribute", "");
            if (argList.Count > 0)
            {
                return $"[{typeName}({string.Join(", ", argList)})]";
            }
            else
            {
                return $"[{typeName}]";
            }*/

        }

        /*private Il2CppTypeEnum ReadAttributeType() {
            Il2CppType enumType = null;
            var type = (Il2CppTypeEnum) this.ReadByte();
            if (type == Il2CppTypeEnum.IL2CPP_TYPE_ENUM)
            {
                var enumTypeIndex = this.ReadCompressedInt32();
                enumType = Assembly.Model.TypesByDefinitionIndex[enumTypeIndex];
                var typeDef = GetTypeDefinitionFromIl2CppType(enumType);
                type = il2Cpp.types[typeDef.elementTypeIndex].type;
            }

            return type;
        }*/
    }

    public static class BinaryReaderExtensions
    {
        public static string ReadString(this BinaryReader reader, int numChars)
        {
            var start = reader.BaseStream.Position;
            // UTF8 takes up to 4 bytes per character
            var str = Encoding.UTF8.GetString(reader.ReadBytes(numChars * 4))[..numChars];
            // make our position what it would have been if we'd known the exact number of bytes needed.
            reader.BaseStream.Position = start;
            reader.ReadBytes(Encoding.UTF8.GetByteCount(str));
            return str;
        }

        public static uint ReadULeb128(this BinaryReader reader)
        {
            uint value = reader.ReadByte();
            if (value >= 0x80)
            {
                var bitshift = 0;
                value &= 0x7f;
                while (true)
                {
                    var b = reader.ReadByte();
                    bitshift += 7;
                    value |= (uint)((b & 0x7f) << bitshift);
                    if (b < 0x80)
                        break;
                }
            }
            return value;
        }

        public static uint ReadCompressedUInt32(this BinaryReader reader)
        {
            uint val;
            var read = reader.ReadByte();

            if ((read & 0x80) == 0)
            {
                // 1 byte written
                val = read;
            }
            else if ((read & 0xC0) == 0x80)
            {
                // 2 bytes written
                val = (read & ~0x80u) << 8;
                val |= reader.ReadByte();
            }
            else if ((read & 0xE0) == 0xC0)
            {
                // 4 bytes written
                val = (read & ~0xC0u) << 24;
                val |= ((uint)reader.ReadByte() << 16);
                val |= ((uint)reader.ReadByte() << 8);
                val |= reader.ReadByte();
            }
            else if (read == 0xF0)
            {
                // 5 bytes written, we had a really large int32!
                val = reader.ReadUInt32();
            }
            else if (read == 0xFE)
            {
                // Special encoding for Int32.MaxValue
                val = uint.MaxValue - 1;
            }
            else if (read == 0xFF)
            {
                // Yes we treat UInt32.MaxValue (and Int32.MinValue, see ReadCompressedInt32) specially
                val = uint.MaxValue;
            }
            else
            {
                throw new Exception("Invalid compressed integer format");
            }

            return val;
        }

        public static int ReadCompressedInt32(this BinaryReader reader)
        {
            var encoded = reader.ReadCompressedUInt32();

            // -UINT32_MAX can't be represted safely in an int32_t, so we treat it specially
            if (encoded == uint.MaxValue)
                return int.MinValue;

            bool isNegative = (encoded & 1) != 0;
            encoded >>= 1;
            if (isNegative)
                return -(int)(encoded + 1);
            return (int)encoded;
        }
    }
}
