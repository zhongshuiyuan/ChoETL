﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ChoETL
{
    internal class ChoFixedLengthRecordWriter : ChoRecordWriter
    {
        private IChoNotifyRecordWrite _callbackRecord;
        private bool _configCheckDone = false;
        private int _index = 0;

        public ChoFixedLengthRecordConfiguration Configuration
        {
            get;
            private set;
        }

        public ChoFixedLengthRecordWriter(Type recordType, ChoFixedLengthRecordConfiguration configuration) : base(recordType)
        {
            ChoGuard.ArgumentNotNull(configuration, "Configuration");
            Configuration = configuration;

            _callbackRecord = ChoMetadataObjectCache.CreateMetadataObject<IChoNotifyRecordWrite>(recordType);

            //Configuration.Validate();
        }

        public override IEnumerable<object> WriteTo(object writer, IEnumerable<object> records, Func<object, bool> predicate = null)
        {
            StreamWriter sw = writer as StreamWriter;
            ChoGuard.ArgumentNotNull(sw, "StreamWriter");

            if (records == null) yield break;

            if (!RaiseBeginWrite(sw))
                yield break;

            CultureInfo prevCultureInfo = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = Configuration.Culture;

            string recText = String.Empty;

            try
            {
                foreach (object record in records)
                {
                    _index++;

                    if (TraceSwitch.TraceVerbose)
                    {
                        if (record is IChoETLNameableObject)
                            ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Writing [{0}] object...".FormatString(((IChoETLNameableObject)record).Name));
                        else
                            ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Writing [{0}] object...".FormatString(_index));
                    }

                    recText = String.Empty;
                    if (record != null)
                    {
                        if (predicate == null || predicate(record))
                        {
                            //Discover and load FixedLength columns from first record
                            if (!_configCheckDone)
                            {
                                string[] fieldNames = null;

                                if (record is ExpandoObject)
                                {
                                    var dict = record as IDictionary<string, Object>;
                                    fieldNames = dict.Keys.ToArray();
                                }
                                else
                                {
                                    fieldNames = ChoTypeDescriptor.GetProperties<ChoFixedLengthRecordFieldAttribute>(record.GetType()).Select(pd => pd.Name).ToArray();
                                    if (fieldNames.Length == 0)
                                    {
                                        fieldNames = ChoType.GetProperties(record.GetType()).Select(p => p.Name).ToArray();
                                    }
                                }

                                Configuration.Validate(fieldNames);

                                WriteHeaderLine(sw);

                                _configCheckDone = true;
                            }

                            if (!RaiseBeforeRecordWrite(record, _index, ref recText))
                                yield break;

                            if (recText == null)
                                continue;
                            else if (recText.Length > 0)
                            {
                                sw.Write("{1}{0}", recText, Configuration.FileHeaderConfiguration.HasHeaderRecord ? Configuration.EOLDelimiter : "");
                                continue;
                            }

                            try
                            {
                                if ((Configuration.ObjectValidationMode & ChoObjectValidationMode.ObjectLevel) == ChoObjectValidationMode.ObjectLevel)
                                    record.DoObjectLevelValidation(Configuration, Configuration.FixedLengthRecordFieldConfigurations);

                                if (ToText(_index, record, out recText))
                                {
                                    if (_index == 1)
                                    {
                                        sw.Write("{1}{0}", recText, Configuration.FileHeaderConfiguration.HasHeaderRecord ? Configuration.EOLDelimiter : "");
                                    }
                                    else
                                        sw.Write("{1}{0}", recText, Configuration.EOLDelimiter);

                                    if (!RaiseAfterRecordWrite(record, _index, recText))
                                        yield break;
                                }
                            }
                            catch (ChoParserException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                ChoETLFramework.HandleException(ex);
                                if (Configuration.ErrorMode == ChoErrorMode.IgnoreAndContinue)
                                {

                                }
                                else if (Configuration.ErrorMode == ChoErrorMode.ReportAndContinue)
                                {
                                    if (!RaiseRecordWriteError(record, _index, recText, ex))
                                        throw;
                                }
                                else
                                    throw;
                            }
                        }
                    }

                    yield return record;
                }
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prevCultureInfo;
            }

            RaiseEndWrite(sw);
        }

        private bool ToText(int index, object rec, out string recText)
        {
            recText = null;
            StringBuilder msg = new StringBuilder();
            object fieldValue = null;
            string fieldText = null;
            ChoFixedLengthRecordFieldConfiguration fieldConfig = null;

            if (Configuration.ColumnCountStrict)
                CheckColumnsStrict(rec);

            //bool firstColumn = true;
            PropertyInfo pi = null;
            foreach (KeyValuePair<string, ChoFixedLengthRecordFieldConfiguration> kvp in Configuration.RecordFieldConfigurationsDict)
            {
                fieldConfig = kvp.Value;
                fieldValue = null;
                fieldText = String.Empty;
                if (Configuration.PIDict != null)
                    Configuration.PIDict.TryGetValue(kvp.Key, out pi);

                if (Configuration.ThrowAndStopOnMissingField)
                {
                    if (rec is ExpandoObject)
                    {
                        var dict = rec as IDictionary<string, Object>;
                        if (!dict.Keys.Contains(kvp.Key, Configuration.FileHeaderConfiguration.StringComparer))
                            throw new ChoMissingRecordFieldException("No matching property found in the object for '{0}' FixedLength column.".FormatString(fieldConfig.FieldName));
                    }
                    else
                    {
                        if (pi == null)
                            throw new ChoMissingRecordFieldException("No matching property found in the object for '{0}' FixedLength column.".FormatString(fieldConfig.FieldName));
                    }
                }

                try
                {
                    if (Configuration.IsDynamicObject)
                    {
                        IDictionary<string, Object> dict = rec as IDictionary<string, Object>;
                        fieldValue = dict.GetValue(kvp.Key, Configuration.FileHeaderConfiguration.IgnoreCase, Configuration.Culture);
                        if (kvp.Value.FieldType == null)
                        {
                            if (fieldValue == null)
                                kvp.Value.FieldType = typeof(string);
                            else
                                kvp.Value.FieldType = fieldValue.GetType();
                        }
                    }
                    else
                    {
                        if (pi != null)
                        {
                            fieldValue = ChoType.GetPropertyValue(rec, pi);
                            if (kvp.Value.FieldType == null)
                                kvp.Value.FieldType = pi.PropertyType;
                        }
                        else
                            kvp.Value.FieldType = typeof(string);
                    }

                    //Discover default value, use it if null
                    if (fieldValue == null)
                    {
                        if (fieldConfig.IsDefaultValueSpecified)
                            fieldValue = fieldConfig.DefaultValue;
                    }

                    if (!RaiseBeforeRecordFieldWrite(rec, index, kvp.Key, ref fieldValue))
                        return false;

                    rec.GetNConvertMemberValue(kvp.Key, kvp.Value, Configuration.Culture, ref fieldValue);

                    if ((Configuration.ObjectValidationMode & ChoObjectValidationMode.ObjectLevel) == ChoObjectValidationMode.MemberLevel)
                        rec.DoMemberLevelValidation(kvp.Key, kvp.Value, Configuration.ObjectValidationMode, fieldValue);

                    if (!RaiseAfterRecordFieldWrite(rec, index, kvp.Key, fieldValue))
                        return false;
                }
                catch (ChoParserException)
                {
                    throw;
                }
                catch (ChoMissingRecordFieldException)
                {
                    if (Configuration.ThrowAndStopOnMissingField)
                        throw;
                }
                catch (Exception ex)    
                {
                    ChoETLFramework.HandleException(ex);

                    if (fieldConfig.ErrorMode == ChoErrorMode.ThrowAndStop)
                        throw;

                    try
                    {
                        if (Configuration.IsDynamicObject)
                        {
                            var dict = rec as IDictionary<string, Object>;

                            if (dict.GetFallbackValue(kvp.Key, kvp.Value, Configuration.Culture, ref fieldValue))
                                dict.DoMemberLevelValidation(kvp.Key, kvp.Value, Configuration.ObjectValidationMode, fieldValue);
                            else if (dict.GetDefaultValue(kvp.Key, kvp.Value, Configuration.Culture, ref fieldValue))
                                dict.DoMemberLevelValidation(kvp.Key, kvp.Value, Configuration.ObjectValidationMode, fieldValue);
                            else
                                throw new ChoWriterException($"Failed to write '{fieldValue}' value for '{fieldConfig.FieldName}' member.", ex);
                        }
                        else if (pi != null)
                        {
                            if (rec.GetFallbackValue(kvp.Key, kvp.Value, Configuration.Culture, ref fieldValue))
                                rec.DoMemberLevelValidation(kvp.Key, kvp.Value, Configuration.ObjectValidationMode);
                            else if (rec.GetDefaultValue(kvp.Key, kvp.Value, Configuration.Culture, ref fieldValue))
                                rec.DoMemberLevelValidation(kvp.Key, kvp.Value, Configuration.ObjectValidationMode, fieldValue);
                            else
                                throw new ChoWriterException($"Failed to write '{fieldValue}' value for '{fieldConfig.FieldName}' member.", ex);
                        }
                        else
                            throw new ChoWriterException($"Failed to write '{fieldValue}' value for '{fieldConfig.FieldName}' member.", ex);
                    }
                    catch (Exception innerEx)
                    {
                        if (ex == innerEx.InnerException)
                        {
                            if (fieldConfig.ErrorMode == ChoErrorMode.IgnoreAndContinue)
                            {
                                continue;
                            }
                            else
                            {
                                if (!RaiseRecordFieldWriteError(rec, index, kvp.Key, fieldText, ex))
                                    throw new ChoWriterException($"Failed to write '{fieldValue}' value of '{kvp.Key}' member.", ex);
                            }
                        }
                        else
                        {
                            throw new ChoWriterException("Failed to use '{0}' fallback value for '{1}' member.".FormatString(fieldValue, kvp.Key), innerEx);
                        }
                    }
                }

                if (fieldValue == null)
                    fieldText = String.Empty;
                else
                    fieldText = fieldValue.ToString();

                msg.Append(NormalizeFieldValue(kvp.Key, fieldText, kvp.Value.Size, kvp.Value.Truncate, kvp.Value.QuoteField, GetFieldValueJustification(kvp.Value.FieldValueJustification, kvp.Value.FieldType), GetFillChar(kvp.Value.FillChar, kvp.Value.FieldType), false));
            }

            recText = msg.ToString();
            return true;
        }

        private ChoFieldValueJustification GetFieldValueJustification(ChoFieldValueJustification? fieldValueJustification, Type fieldType)
        {
            if (fieldValueJustification != null)
                return fieldValueJustification.Value;

            if (fieldType == typeof(int)
              || fieldType == typeof(uint)
              || fieldType == typeof(long)
              || fieldType == typeof(ulong)
              || fieldType == typeof(short)
              || fieldType == typeof(ushort)
              || fieldType == typeof(byte)
              || fieldType == typeof(sbyte)
              || fieldType == typeof(float)
              || fieldType == typeof(double)
              || fieldType == typeof(decimal)
              )
            {
                return ChoFieldValueJustification.Right;
            }
            else
                return ChoFieldValueJustification.Left;
        }

        private char GetFillChar(char? fillChar, Type fieldType)
        {
            if (fillChar != null)
                return fillChar.Value;

            if (fieldType == typeof(int)
                || fieldType == typeof(uint)
                || fieldType == typeof(long)
                || fieldType == typeof(ulong)
                || fieldType == typeof(short)
                || fieldType == typeof(ushort)
                || fieldType == typeof(byte)
                || fieldType == typeof(sbyte)
                || fieldType == typeof(float)
                || fieldType == typeof(double)
                || fieldType == typeof(decimal)
                )
            {
                return '0';
            }
            else
                return ' ';
        }

        private void CheckColumnsStrict(object rec)
        {
            if (rec is ExpandoObject)
            {
                var eoDict = rec as IDictionary<string, Object>;

                if (eoDict.Count != Configuration.FixedLengthRecordFieldConfigurations.Count)
                    throw new ChoParserException("Incorrect number of fields found in record object. Expected [{0}] fields. Found [{1}] fields.".FormatString(Configuration.FixedLengthRecordFieldConfigurations.Count, eoDict.Count));

                string[] missingColumns = Configuration.FixedLengthRecordFieldConfigurations.Select(v => v.Name).Except(eoDict.Keys, Configuration.FileHeaderConfiguration.StringComparer).ToArray();
                if (missingColumns.Length > 0)
                    throw new ChoParserException("[{0}] fields are not found in record object.".FormatString(String.Join(",", missingColumns)));
            }
            else
            {
                PropertyDescriptor[] pds = ChoTypeDescriptor.GetProperties<ChoFixedLengthRecordFieldAttribute>(rec.GetType()).ToArray();

                if (pds.Length != Configuration.FixedLengthRecordFieldConfigurations.Count)
                    throw new ChoParserException("Incorrect number of fields found in record object. Expected [{0}] fields. Found [{1}] fields.".FormatString(Configuration.FixedLengthRecordFieldConfigurations.Count, pds.Length));

                string[] missingColumns = Configuration.FixedLengthRecordFieldConfigurations.Select(v => v.Name).Except(pds.Select(pd => pd.Name), Configuration.FileHeaderConfiguration.StringComparer).ToArray();
                if (missingColumns.Length > 0)
                    throw new ChoParserException("[{0}] fields are not found in record object.".FormatString(String.Join(",", missingColumns)));
            }
        }

        private void WriteHeaderLine(StreamWriter sw)
        {
            if (Configuration.FileHeaderConfiguration.HasHeaderRecord)
            {
                string header = ToHeaderText();
                if (header.IsNullOrWhiteSpace())
                    return;

                sw.Write(header);
            }
        }

        private string ToHeaderText()
        {
            StringBuilder msg = new StringBuilder();
            string value;
            foreach (var member in Configuration.FixedLengthRecordFieldConfigurations)
            {
                value = NormalizeFieldValue(member.Name, member.FieldName, member.Size,
                    Configuration.FileHeaderConfiguration.Truncate == null ? true : Configuration.FileHeaderConfiguration.Truncate.Value,
                        false,
                        Configuration.FileHeaderConfiguration.Justification == null ? ChoFieldValueJustification.Left : Configuration.FileHeaderConfiguration.Justification.Value,
                        Configuration.FileHeaderConfiguration.FillChar == null ? ' ' : Configuration.FileHeaderConfiguration.FillChar.Value,
                        true);

                msg.Append(value);
            }
            return msg.ToString();
        }

        private string NormalizeFieldValue(string fieldName, string fieldValue, int? size, bool truncate, bool? quoteField,
            ChoFieldValueJustification fieldValueJustification, char fillChar, bool isHeader = false)
        {
            string lFieldValue = fieldValue;
            bool retValue = false;

            if (retValue)
                return lFieldValue;

            if (fieldValue.IsNull())
                fieldValue = String.Empty;

            if (quoteField == null || !quoteField.Value)
            {
                if (fieldValue.StartsWith("\"") && fieldValue.EndsWith("\""))
                {

                }
                else
                {
                    if (fieldValue.Contains(Configuration.EOLDelimiter))
                    {
                        if (isHeader)
                            throw new ChoParserException("Field header '{0}' value contains EOL delimiter character.".FormatString(fieldName));
                        else
                            throw new ChoParserException("Field '{0}' value contains EOL delimiter character.".FormatString(fieldName));
                    }
                }
            }
            else
            {
                if (fieldValue.StartsWith("\"") && fieldValue.EndsWith("\""))
                {

                }
                else
                {
                    fieldValue = "\"{0}\"".FormatString(fieldValue);
                }
            }

            if (size != null)
            {
                if (fieldValue.Length < size.Value)
                {
                    if (fillChar != ChoCharEx.NUL)
                    {
                        if (fieldValueJustification == ChoFieldValueJustification.Right)
                            fieldValue = fieldValue.PadLeft(size.Value, fillChar);
                        else if (fieldValueJustification == ChoFieldValueJustification.Left)
                            fieldValue = fieldValue.PadRight(size.Value, fillChar);
                    }
                }
                else if (fieldValue.Length > size.Value)
                {
                    if (truncate)
                        fieldValue = fieldValue.Substring(0, size.Value);
                    else
                    {
                        if (isHeader)
                            throw new ApplicationException("Field header value length overflowed for '{0}' member [Expected: {1}, Actual: {2}].".FormatString(fieldName, size, fieldValue.Length));
                        else
                            throw new ApplicationException("Field value length overflowed for '{0}' member [Expected: {1}, Actual: {2}].".FormatString(fieldName, size, fieldValue.Length));
                    }
                }
            }

            return fieldValue;
        }

        private bool RaiseBeginWrite(object state)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.BeginWrite(state), true);
        }

        private void RaiseEndWrite(object state)
        {
            if (_callbackRecord == null) return;
            ChoActionEx.RunWithIgnoreError(() => _callbackRecord.EndWrite(state));
        }

        private bool RaiseBeforeRecordWrite(object target, int index, ref string state)
        {
            if (_callbackRecord == null) return true;
            object inState = state;
            bool retValue = ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.BeforeRecordWrite(target, index, ref inState), true);
            if (retValue)
                state = inState == null ? null : inState.ToString();

            return retValue;
        }

        private bool RaiseAfterRecordWrite(object target, int index, string state)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.AfterRecordWrite(target, index, state), true);
        }

        private bool RaiseRecordWriteError(object target, int index, string state, Exception ex)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.RecordWriteError(target, index, state, ex), false);
        }

        private bool RaiseBeforeRecordFieldWrite(object target, int index, string propName, ref object value)
        {
            if (_callbackRecord == null) return true;
            object state = value;
            bool retValue = ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.BeforeRecordFieldWrite(target, index, propName, ref state), true);

            if (retValue)
                value = state;

            return retValue;
        }

        private bool RaiseAfterRecordFieldWrite(object target, int index, string propName, object value)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.AfterRecordFieldWrite(target, index, propName, value), true);
        }

        private bool RaiseRecordFieldWriteError(object target, int index, string propName, object value, Exception ex)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.RecordFieldWriteError(target, index, propName, value, ex), true);
        }
    }
}
