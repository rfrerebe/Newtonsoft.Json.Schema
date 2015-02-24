﻿#region License
// Copyright (c) Newtonsoft. All Rights Reserved.
// License: https://raw.github.com/JamesNK/Newtonsoft.Json.Schema/master/LICENSE.md
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema.Infrastructure.Discovery;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Utilities;

namespace Newtonsoft.Json.Schema.Infrastructure
{
    internal class JSchemaWriter
    {
        private readonly JsonWriter _writer;
        private readonly List<KnownSchema> _knownSchemas;
        private readonly IList<ExternalSchema> _externalSchemas;

        public JSchemaWriter(JsonWriter writer)
            : this(writer, null)
        {
        }

        public JSchemaWriter(JsonWriter writer, JSchemaWriterSettings settings)
        {
            ValidationUtils.ArgumentNotNull(writer, "writer");

            _writer = writer;
            _knownSchemas = new List<KnownSchema>();

            if (settings != null)
            {
                _externalSchemas = settings.ExternalSchemas;
            }
        }

        private void ReferenceOrWriteSchema(JSchema context, JSchema schema)
        {
            KnownSchema knownSchema = _knownSchemas.Single(s => s.Schema == schema);

            if (knownSchema.State != KnownSchemaState.InlinePending)
            {
                KnownSchema currentKnownSchema = _knownSchemas.Single(s => s.Schema == context);

                Uri reference;

                // Id is fully qualified
                // make it relative to the current schema
                if (currentKnownSchema.Id.IsBaseOf(knownSchema.Id))
                {
                    reference = currentKnownSchema.Id.MakeRelativeUri(knownSchema.Id);

                    // MakeRelativeUri escapes the result, need to unescape
                    reference = new Uri(Uri.UnescapeDataString(reference.OriginalString), UriKind.RelativeOrAbsolute);
                }
                else if (knownSchema.Id == currentKnownSchema.Id)
                {
                    reference = new Uri("#", UriKind.RelativeOrAbsolute);
                }
                else
                {
                    reference = knownSchema.Id;
                }

                WriteReferenceObject(reference);
                return;
            }

            knownSchema.State = KnownSchemaState.InlineWritten;

            WriteSchemaInternal(schema);
        }

        private void WriteReferenceObject(Uri reference)
        {
            _writer.WriteStartObject();
            _writer.WritePropertyName(JsonTypeReflector.RefPropertyName);
            _writer.WriteValue(reference);
            _writer.WriteEndObject();
        }

        private void WriteToken(JSchema context, JsonWriter writer, JToken token)
        {
            if (token is JObject)
            {
                JObject o = (JObject)token;

                JSchemaAnnotation schemaAnnotation = o.Annotation<JSchemaAnnotation>();

                if (schemaAnnotation != null)
                {
                    ReferenceOrWriteSchema(context, schemaAnnotation.Schema);
                }
                else
                {
                    writer.WriteStartObject();

                    foreach (JProperty property in o.Properties())
                    {
                        writer.WritePropertyName(property.Name);

                        JToken value = property.Value;
                        if (value != null)
                            WriteToken(context, writer, value);
                        else
                            writer.WriteNull();
                    }

                    writer.WriteEndObject();
                }
            }
            else if (token is JArray)
            {
                JArray a = (JArray)token;

                writer.WriteStartArray();

                for (int i = 0; i < a.Count; i++)
                {
                    WriteToken(context, writer, a[i]);
                }

                writer.WriteEndArray();
            }
            else if (token is JConstructor)
            {
                JConstructor c = (JConstructor)token;

                writer.WriteStartConstructor(c.Name);

                foreach (JToken t in c.Children())
                {
                    WriteToken(context, writer, t);
                }

                writer.WriteEndConstructor();
            }
            else if (token is JValue)
            {
                token.WriteTo(writer);
            }
        }

        public void WriteSchema(JSchema schema)
        {
            ValidationUtils.ArgumentNotNull(schema, "schema");

            _knownSchemas.Clear();

            JSchemaDiscovery discovery;

            if (_externalSchemas != null)
            {
                foreach (ExternalSchema externalSchema in _externalSchemas)
                {
                    discovery = new JSchemaDiscovery(_knownSchemas, KnownSchemaState.External);
                    discovery.Discover(externalSchema.Schema, externalSchema.Uri);
                }
            }

            discovery = new JSchemaDiscovery(_knownSchemas, KnownSchemaState.InlinePending);
            discovery.Discover(schema, null);

            KnownSchema rootKnownSchema = _knownSchemas.Single(s => s.Schema == schema);
            rootKnownSchema.State = KnownSchemaState.InlineWritten;

            WriteSchemaInternal(schema);
        }

        private void WriteSchemaInternal(JSchema schema)
        {
            _writer.WriteStartObject();

            WritePropertyIfNotNull(_writer, Constants.PropertyNames.Id, schema.Id);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.Title, schema.Title);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.Description, schema.Description);

            if (schema._extensionData != null)
            {
                foreach (KeyValuePair<string, JToken> extensionDataPair in schema._extensionData)
                {
                    _writer.WritePropertyName(extensionDataPair.Key);
                    WriteToken(schema, _writer, extensionDataPair.Value);
                }
            }

            if (schema.Type != null)
                WriteType(Constants.PropertyNames.Type, _writer, schema.Type.Value);

            if (schema.Default != null)
            {
                _writer.WritePropertyName(Constants.PropertyNames.Default);
                schema.Default.WriteTo(_writer);
            }
            if (!schema.AllowAdditionalProperties)
            {
                _writer.WritePropertyName(Constants.PropertyNames.AdditionalProperties);
                _writer.WriteValue(schema.AllowAdditionalProperties);
            }
            else
            {
                if (schema.AdditionalProperties != null)
                {
                    _writer.WritePropertyName(Constants.PropertyNames.AdditionalProperties);
                    ReferenceOrWriteSchema(schema, schema.AdditionalProperties);
                }
            }
            if (!schema.AllowAdditionalItems)
            {
                _writer.WritePropertyName(Constants.PropertyNames.AdditionalItems);
                _writer.WriteValue(schema.AllowAdditionalItems);
            }
            else
            {
                if (schema.AdditionalItems != null)
                {
                    _writer.WritePropertyName(Constants.PropertyNames.AdditionalItems);
                    ReferenceOrWriteSchema(schema, schema.AdditionalItems);
                }
            }
            WriteSchemaDictionaryIfNotNull(schema, _writer, Constants.PropertyNames.Properties, schema._properties);
            WriteRequired(schema);
            WriteSchemaDictionaryIfNotNull(schema, _writer, Constants.PropertyNames.PatternProperties, schema._patternProperties);
            WriteItems(schema);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.Minimum, schema.Minimum);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.Maximum, schema.Maximum);
            WritePropertyIfNotDefault(_writer, Constants.PropertyNames.ExclusiveMinimum, schema.ExclusiveMinimum);
            WritePropertyIfNotDefault(_writer, Constants.PropertyNames.ExclusiveMaximum, schema.ExclusiveMaximum);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.MinimumLength, schema.MinimumLength);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.MaximumLength, schema.MaximumLength);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.MinimumItems, schema.MinimumItems);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.MaximumItems, schema.MaximumItems);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.MinimumProperties, schema.MinimumProperties);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.MaximumProperties, schema.MaximumProperties);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.MultipleOf, schema.MultipleOf);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.Pattern, schema.Pattern);
            WritePropertyIfNotNull(_writer, Constants.PropertyNames.Format, schema.Format);
            if (schema._enum != null && schema._enum.Count > 0)
            {
                _writer.WritePropertyName(Constants.PropertyNames.Enum);
                _writer.WriteStartArray();
                foreach (JToken token in schema._enum)
                {
                    token.WriteTo(_writer);
                }
                _writer.WriteEndArray();
            }
            WriteSchemas(schema, schema._allOf, Constants.PropertyNames.AllOf);
            WriteSchemas(schema, schema._anyOf, Constants.PropertyNames.AnyOf);
            WriteSchemas(schema, schema._oneOf, Constants.PropertyNames.OneOf);
            WriteSchema(schema, schema.Not, Constants.PropertyNames.Not);

            _writer.WriteEndObject();
        }

        private void WriteRequired(JSchema schema)
        {
            if (schema._required != null && schema._required.Count > 0)
            {
                _writer.WritePropertyName(Constants.PropertyNames.Required);
                _writer.WriteStartArray();
                foreach (string s in schema._required)
                {
                    _writer.WriteValue(s);
                }
                _writer.WriteEndArray();
            }
        }

        private void WriteSchema(JSchema context, JSchema schema, string name)
        {
            if (schema != null)
            {
                _writer.WritePropertyName(name);
                ReferenceOrWriteSchema(context, schema);
            }
        }

        private void WriteSchemas(JSchema context, List<JSchema> schemas, string name)
        {
            if (schemas != null && schemas.Count > 0)
            {
                _writer.WritePropertyName(name);
                _writer.WriteStartArray();
                foreach (JSchema s in schemas)
                {
                    ReferenceOrWriteSchema(context, s);
                }
                _writer.WriteEndArray();
            }
        }

        private void WriteItems(JSchema schema)
        {
            if ((schema._items == null || schema._items.Count == 0) && !schema.ItemsPositionValidation)
                return;

            _writer.WritePropertyName(Constants.PropertyNames.Items);

            if (!schema.ItemsPositionValidation)
            {
                if (schema._items != null && schema._items.Count > 0)
                {
                    ReferenceOrWriteSchema(schema, schema._items[0]);
                }
                else
                {
                    _writer.WriteStartObject();
                    _writer.WriteEndObject();
                }
            }
            else
            {
                _writer.WriteStartArray();
                if (schema._items != null)
                {
                    for (int i = 0; i < schema._items.Count; i++)
                    {
                        var itemSchema = schema._items[i];

                        ReferenceOrWriteSchema(schema, itemSchema);
                    }
                }
                _writer.WriteEndArray();
            }
        }

        private void WriteSchemaDictionaryIfNotNull(JSchema context, JsonWriter writer, string propertyName, IDictionary<string, JSchema> properties)
        {
            if (properties != null && properties.Count > 0)
            {
                writer.WritePropertyName(propertyName);

                writer.WriteStartObject();
                foreach (KeyValuePair<string, JSchema> property in properties)
                {
                    writer.WritePropertyName(property.Key);

                    ReferenceOrWriteSchema(context, property.Value);
                }
                writer.WriteEndObject();
            }
        }

        private void WriteType(string propertyName, JsonWriter writer, JSchemaType type)
        {
            IList<JSchemaType> types;
            if (Enum.IsDefined(typeof(JSchemaType), type))
                types = new List<JSchemaType> { type };
            else
                types = EnumUtils.GetFlagsValues(type).Where(v => v != JSchemaType.None).ToList();

            if (types.Count == 0)
                return;

            writer.WritePropertyName(propertyName);

            if (types.Count == 1)
            {
                writer.WriteValue(JSchemaTypeHelpers.MapType(types[0]));
                return;
            }

            writer.WriteStartArray();
            foreach (JSchemaType schemaType in types)
            {
                writer.WriteValue(JSchemaTypeHelpers.MapType(schemaType));
            }
            writer.WriteEndArray();
        }

        private void WritePropertyIfNotDefault<T>(JsonWriter writer, string propertyName, T value, T defaultValue = default(T))
        {
            if (!value.Equals(defaultValue))
            {
                writer.WritePropertyName(propertyName);
                writer.WriteValue(value);
            }
        }

        private void WritePropertyIfNotNull(JsonWriter writer, string propertyName, object value)
        {
            if (value != null)
            {
                writer.WritePropertyName(propertyName);
                writer.WriteValue(value);
            }
        }
    }
}