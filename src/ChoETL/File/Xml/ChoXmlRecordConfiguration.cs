﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace ChoETL
{
    [DataContract]
    public class ChoXmlRecordConfiguration : ChoFileRecordConfiguration
    {
        [DataMember]
        public List<ChoXmlRecordFieldConfiguration> XmlRecordFieldConfigurations
        {
            get;
            private set;
        }
        [DataMember]
        public string XPath
        {
            get;
            set;
        }
        [DataMember]
        public int Indent
        {
            get;
            set;
        }
        [DataMember]
        public char IndentChar
        {
            get;
            set;
        }
        public XmlNamespaceManager NamespaceManager
        {
            get;
            set;
        }

        public XmlSerializer XmlSerializer
        {
            get;
            set;
        }
        [DataMember]
        public bool UseXmlSerialization
        {
            get;
            set;
        }
        [DataMember]
        public ChoNullValueHandling NullValueHandling
        {
            get;
            set;
        }
        [DataMember]
        public string XmlVersion { get; set; }
        [DataMember]
        public bool OmitXmlDeclaration { get; set; }
        internal Dictionary<string, ChoXmlRecordFieldConfiguration> RecordFieldConfigurationsDict
        {
            get;
            private set;
        }
        internal bool IsComplexXPathUsed = true;
        internal string RootName;
        internal string NodeName;

        public ChoXmlRecordFieldConfiguration this[string name]
        {
            get
            {
                return XmlRecordFieldConfigurations.Where(i => i.Name == name).FirstOrDefault();
            }
        }

        public ChoXmlRecordConfiguration() : this(null)
        {

        }

        internal ChoXmlRecordConfiguration(Type recordType) : base(recordType)
        {
            XmlRecordFieldConfigurations = new List<ChoXmlRecordFieldConfiguration>();

            XmlVersion = "1.0";
            OmitXmlDeclaration = true;
            Indent = 2;
            IndentChar = ' ';
            if (recordType != null)
            {
                Init(recordType);
            }

            if (XPath.IsNullOrEmpty())
            {
                //XPath = "//*";
            }
            NamespaceManager = new XmlNamespaceManager(new NameTable());
        }

        protected override void Init(Type recordType)
        {
            base.Init(recordType);

            ChoXmlRecordObjectAttribute recObjAttr = ChoType.GetAttribute<ChoXmlRecordObjectAttribute>(recordType);
            if (recObjAttr != null)
            {
            }

            DiscoverRecordFields(recordType);
        }

        internal void UpdateFieldTypesIfAny(Dictionary<string, Type> dict)
        {
            if (dict == null)
                return;

            foreach (var key in dict.Keys)
            {
                if (RecordFieldConfigurationsDict.ContainsKey(key) && dict[key] != null)
                    RecordFieldConfigurationsDict[key].FieldType = dict[key];
            }
        }

        public override void MapRecordFields<T>()
        {
            DiscoverRecordFields(typeof(T));
        }

        public override void MapRecordFields(Type recordType)
        {
            DiscoverRecordFields(recordType);
        }

        private void DiscoverRecordFields(Type recordType)
        {
            if (!IsDynamicObject)
            {
                XmlRecordFieldConfigurations.Clear();

                if (ChoTypeDescriptor.GetProperties(recordType).Where(pd => pd.Attributes.OfType<ChoXmlNodeRecordFieldAttribute>().Any()).Any())
                {
                    foreach (PropertyDescriptor pd in ChoTypeDescriptor.GetProperties(recordType).Where(pd => pd.Attributes.OfType<ChoXmlNodeRecordFieldAttribute>().Any()))
                    {
                        //if (!pd.PropertyType.IsSimple())
                        //    throw new ChoRecordConfigurationException("Property '{0}' is not a simple type.".FormatString(pd.Name));

                        var obj = new ChoXmlRecordFieldConfiguration(pd.Name, pd.Attributes.OfType<ChoXmlNodeRecordFieldAttribute>().First());
                        if (obj.XPath.IsNullOrWhiteSpace())
                            obj.XPath = $"//{obj.FieldName}|//@{obj.FieldName}";

                        obj.FieldType = pd.PropertyType;
                        XmlRecordFieldConfigurations.Add(obj);
                    }
                }
                else
                {
                    foreach (PropertyDescriptor pd in ChoTypeDescriptor.GetProperties(recordType))
                    {
                        //if (!pd.PropertyType.IsSimple())
                        //    throw new ChoRecordConfigurationException("Property '{0}' is not a simple type.".FormatString(pd.Name));

                        var obj = new ChoXmlRecordFieldConfiguration(pd.Name, $"//{pd.Name}|//@{pd.Name}");
                        obj.FieldType = pd.PropertyType;
                        XmlRecordFieldConfigurations.Add(obj);
                    }
                }
            }
        }

        public override void Validate(object state)
        {
            base.Validate(state);

            //if (XPath.IsNull())
            //    throw new ChoRecordConfigurationException("XPath can't be null or whitespace.");

            if (XPath.IsNullOrWhiteSpace())
            {
                if (!IsDynamicObject && (RecordType.IsGenericType && RecordType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
                {
                    NodeName = "KeyValuePair";
                    RootName = "KeyValuePairs";
                }
                else if (!IsDynamicObject && !typeof(IChoScalarObject).IsAssignableFrom(RecordType))
                {
                    NodeName = RecordType.Name;
                    RootName = NodeName.ToPlural();
                }
            }
            else
            {
                RootName = XPath.SplitNTrim("/").Where(t => !t.IsNullOrWhiteSpace() && t.NTrim() != "." && t.NTrim() != ".." && t.NTrim() != "*").FirstOrDefault();
                NodeName = XPath.SplitNTrim("/").Where(t => !t.IsNullOrWhiteSpace() && t.NTrim() != "." && t.NTrim() != ".." && t.NTrim() != "*").Skip(1).FirstOrDefault();
            }

            if (RootName.IsNullOrWhiteSpace())
            {
                ChoXmlDocumentRootAttribute da = TypeDescriptor.GetAttributes(RecordType).OfType<ChoXmlDocumentRootAttribute>().FirstOrDefault();
                if (da != null)
                    NodeName = da.Name;
                if (RootName.IsNullOrWhiteSpace())
                {
                    RootName = "Root";
                }
            }

            if (NodeName.IsNullOrWhiteSpace())
            {
                if (!IsDynamicObject)
                {
                    XmlRootAttribute ra = TypeDescriptor.GetAttributes(RecordType).OfType<XmlRootAttribute>().FirstOrDefault();
                    if (ra != null)
                        NodeName = ra.ElementName;
                }

                if (NodeName.IsNullOrWhiteSpace())
                    NodeName = "XElement";
            }

            //Encode Root and node names
            RootName = System.Net.WebUtility.HtmlEncode(RootName);
            NodeName = System.Net.WebUtility.HtmlEncode(NodeName);

            string[] fieldNames = null;
            XElement xpr = null;
            if (state is Tuple<long, XElement>)
                xpr = ((Tuple<long, XElement>)state).Item2;
            else
                fieldNames = state as string[];

            if (AutoDiscoverColumns
                && XmlRecordFieldConfigurations.Count == 0)
            {
                if (RecordType != null && !IsDynamicObject
                    && ChoTypeDescriptor.GetProperties(RecordType).Where(pd => pd.Attributes.OfType<ChoXmlNodeRecordFieldAttribute>().Any()).Any())
                {
                    IsComplexXPathUsed = false;

                    long startIndex = 0;
                    long size = 0;
                    string xpath = null;
                    bool useCache = true;
                    ChoXmlNodeRecordFieldAttribute attr = null;
                    foreach (PropertyDescriptor pd in ChoTypeDescriptor.GetProperties(RecordType).Where(pd => pd.Attributes.OfType<ChoXmlNodeRecordFieldAttribute>().Any()))
                    {
                        //if (!pd.PropertyType.IsSimple())
                        //    throw new ChoRecordConfigurationException("Property '{0}' is not a simple type.".FormatString(pd.Name));

                        attr = ChoTypeDescriptor.GetPropetyAttribute<ChoXmlNodeRecordFieldAttribute>(pd);
                        if (attr.XPath.IsNullOrEmpty())
                        {
                            xpath = $"//{pd.Name}|//@{pd.Name}";
                            IsComplexXPathUsed = true;
                        }
                        else
                            useCache = false;

                        var obj = new ChoXmlRecordFieldConfiguration(pd.Name, xpath);
                        obj.FieldType = pd.PropertyType;
                        obj.UseCache = useCache;
                        //if (!obj.IsCollection)
                        //    obj.IsCollection = typeof(ICollection).IsAssignableFrom(obj.FieldType);
                        XmlRecordFieldConfigurations.Add(obj);

                        startIndex += size;
                    }

                    //RecordLength = startIndex;
                }
                else if (xpr != null)
                {
                    IsComplexXPathUsed = false;
                    ChoXmlNamespaceManager nsMgr = new ChoXmlNamespaceManager(NamespaceManager);

                    Dictionary<string, ChoXmlRecordFieldConfiguration> dict = new Dictionary<string, ChoXmlRecordFieldConfiguration>(StringComparer.CurrentCultureIgnoreCase);
                    string name = null;
                    foreach (var attr in xpr.Attributes())
                    {
                        //if (!attr.Name.NamespaceName.IsNullOrWhiteSpace()) continue;

                        name = GetNameWithNamespace(xpr.Name, attr.Name);

                        if (!dict.ContainsKey(name))
                            dict.Add(name, new ChoXmlRecordFieldConfiguration(name, $"//@{name}")); // DefaultNamespace.IsNullOrWhiteSpace() ? $"//@{name}" : $"//@{DefaultNamespace}" + ":" + $"{name}") { IsXmlAttribute = true });
                        else
                        {
                            throw new ChoRecordConfigurationException("Duplicate field(s) [Name(s): {0}] found.".FormatString(name));
                        }
                    }

                    bool hasElements = false;
                    var z = xpr.Elements().ToArray();
                    foreach (var ele in xpr.Elements())
                    {
                        name = GetNameWithNamespace(ele.Name);

                        hasElements = true;
                        if (!dict.ContainsKey(name))
                            dict.Add(name, new ChoXmlRecordFieldConfiguration(name, $"//{name}")); // DefaultNamespace.IsNullOrWhiteSpace() ? $"//{name}" : $"//{DefaultNamespace}" + ":" + $"{name}"));
                        else
                        {
                            if (dict[name].IsXmlAttribute)
                                throw new ChoRecordConfigurationException("Duplicate field(s) [Name(s): {0}] found.".FormatString(name));

                            dict[name].IsArray = true;
                        }
                    }

                    if (!hasElements)
                    {
                        name = xpr.Name.LocalName;
                        dict.Add(name, new ChoXmlRecordFieldConfiguration(name, "text()"));
                    }

                    foreach (ChoXmlRecordFieldConfiguration obj in dict.Values)
                        XmlRecordFieldConfigurations.Add(obj);
                }
                else if (!fieldNames.IsNullOrEmpty())
                {
                    foreach (string fn in fieldNames)
                    {
                        var obj = new ChoXmlRecordFieldConfiguration(fn, xPath: $"//{fn}");
                        XmlRecordFieldConfigurations.Add(obj);
                    }
                }
            }
            else
            {
                IsComplexXPathUsed = false;

                foreach (var fc in XmlRecordFieldConfigurations)
                {
                    if (fc.IsArray == null)
                        fc.IsArray = typeof(ICollection).IsAssignableFrom(fc.FieldType);

                    if (fc.XPath.IsNullOrWhiteSpace())
                        fc.XPath = $"//{fc.FieldName}|//@{fc.FieldName}";
                    else
                    {
                        if (fc.XPath == fc.FieldName
                            || fc.XPath == $"//{fc.FieldName}" || fc.XPath == $"/{fc.FieldName}" || fc.XPath == $"./{fc.FieldName}"
                            || fc.XPath == $"//@{fc.FieldName}" || fc.XPath == $"/@{fc.FieldName}" || fc.XPath == $"./@{fc.FieldName}"
                            )
                        {

                        }
                        else
                        {
                            IsComplexXPathUsed = true;
                            fc.UseCache = false;
                        }
                    }
                }
            }

            if (XmlRecordFieldConfigurations.Count <= 0)
                throw new ChoRecordConfigurationException("No record fields specified.");

            //Validate each record field
            foreach (var fieldConfig in XmlRecordFieldConfigurations)
                fieldConfig.Validate(this);

            //Check field position for duplicate
            string[] dupFields = XmlRecordFieldConfigurations.GroupBy(i => i.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key).ToArray();

            if (dupFields.Length > 0)
                throw new ChoRecordConfigurationException("Duplicate field(s) [Name(s): {0}] found.".FormatString(String.Join(",", dupFields)));

            RecordFieldConfigurationsDict = XmlRecordFieldConfigurations.OrderBy(c => c.IsXmlAttribute).Where(i => !i.Name.IsNullOrWhiteSpace()).ToDictionary(i => i.Name);

            if (XmlRecordFieldConfigurations.Where(e => e.IsNullable).Any()
                || NullValueHandling == ChoNullValueHandling.Empty)
            {
                if (!NamespaceManager.HasNamespace("xsi"))
                    NamespaceManager.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");
                if (!NamespaceManager.HasNamespace("xsd"))
                    NamespaceManager.AddNamespace("xsd", "http://www.w3.org/2001/XMLSchema");
            }

            LoadNCacheMembers(XmlRecordFieldConfigurations);
        }

        internal string GetNameWithNamespace(XName name)
        {
            ChoXmlNamespaceManager nsMgr = new ChoXmlNamespaceManager(NamespaceManager);

            if (!name.NamespaceName.IsNullOrWhiteSpace())
            {
                string prefix = nsMgr.GetPrefixOfNamespace(name.NamespaceName);
                if (prefix.IsNullOrWhiteSpace()) return name.LocalName;

                return prefix + ":" + name.LocalName;
            }
            else
                return name.LocalName;
        }

        internal string GetNameWithNamespace(XName name, XName propName)
        {
            ChoXmlNamespaceManager nsMgr = new ChoXmlNamespaceManager(NamespaceManager);

            if (!name.NamespaceName.IsNullOrWhiteSpace())
            {
                string prefix = nsMgr.GetPrefixOfNamespace(name.NamespaceName);
                if (prefix.IsNullOrWhiteSpace()) return propName.LocalName;

                return prefix + ":" + propName.LocalName;
            }
            else
                return propName.LocalName;
        }

        public ChoXmlRecordConfiguration Configure(Action<ChoXmlRecordConfiguration> action)
        {
            if (action != null)
                action(this);

            return this;
        }
    }

    public class ChoXmlNamespaceManager
    {
        public readonly IDictionary<string, string> NSDict;

        public ChoXmlNamespaceManager(XmlNamespaceManager nsMgr)
        {
            NSDict = nsMgr.GetNamespacesInScope(XmlNamespaceScope.All);
        }

        public string GetPrefixOfNamespace(string ns)
        {
            return NSDict.Where(kvp => kvp.Value == ns).Select(kvp => kvp.Key).FirstOrDefault();
        }

        public string GetNamespaceForPrefix(string prefix)
        {
            if (NSDict.ContainsKey(prefix))
                return NSDict[prefix];
            else
                return null;
        }
    }
}
