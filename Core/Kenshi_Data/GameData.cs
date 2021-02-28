﻿using Core;
using Core.Kenshi_Data.Enums;
using Core.Kenshi_Data.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Core
{
    public class GameData
    {
        public static Desc nullDesc = new Desc();

        public struct quat
        {
            public float w { get; }
            public float x { get; }
            public float y { get; }
            public float z { get; }

            public quat(float w, float x, float y, float z)
            {
                this.w = 1f;
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public override string ToString()
            {
                return string.Format("{0} {1} {2} {3}", this.w, this.x, this.y, this.z);
            }
        }

        public struct vec
        {
            public vec(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public float x { get; }
            public float y { get; }
            public float z { get; }


            public override string ToString()
            {
                return string.Format("{0} {1} {2}", this.x, (object)this.y, (object)this.z);
            }
        }

        public static SortedList<ItemType, SortedList<string, Desc>> desc = new SortedList<ItemType, SortedList<string, Desc>>();

        public void resolveAllReferences()
        {
            Parallel.ForEach((IEnumerable<KeyValuePair<string, Item>>)this.items, new ParallelOptions()
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, (Action<KeyValuePair<string, Item>>)(item => item.Value.resolveReferences(this)));
        }

        public static Desc getDesc(ItemType type, string name)
        {
            return desc.ContainsKey(type) && desc[type].ContainsKey(name) ? desc[type][name] : nullDesc;
        }

        public class Reference
        {
            public static TripleInt Removed = new TripleInt(int.MaxValue, int.MaxValue, int.MaxValue);
            public string sID = "";
            public Item item;
            public TripleInt original;
            public TripleInt mod;
            public TripleInt locked;

            public Reference(string id, TripleInt value = null)
            {
                this.sID = id;
                this.original = value;
            }

            public string itemID
            {
                get
                {
                    return this.item != null ? this.item.stringID : this.sID;
                }
            }

            public TripleInt Values
            {
                get
                {
                    if (this.locked != null)
                        return this.locked;
                    return this.mod == null ? this.original : this.mod;
                }
            }
        }

        public class TripleInt
        {
            public int v0;
            public int v1;
            public int v2;

            public TripleInt(TripleInt v)
            {
                this.v0 = v.v0;
                this.v1 = v.v1;
                this.v2 = v.v2;
            }

            public TripleInt(int i0 = 0, int i1 = 0, int i2 = 0)
            {
                this.v0 = i0;
                this.v1 = i1;
                this.v2 = i2;
            }

            public bool Equals(TripleInt b)
            {
                return b != null && this.v0 == b.v0 && this.v1 == b.v1 && this.v2 == b.v2;
            }
        }

        public Dictionary<string, Item> items;
        public Header header { get; set; }
        public int lastID { get; set; }
        public string Signature { get; set; }
        public string Filename { get; set; }

        public static byte[] StrByteBuffer = new byte[4096];
        public IEnumerable<KeyValuePair<string, ArrayList>> references;

        public static string readString(BinaryReader file)
        {
            int count = file.ReadInt32();
            if (count <= 0)
                return string.Empty;
            if (count > StrByteBuffer.Length)
                Array.Resize(ref StrByteBuffer, StrByteBuffer.Length * 2);
            file.Read(StrByteBuffer, 0, count);
            return Encoding.UTF8.GetString(StrByteBuffer, 0, count);
        }

        public static Header readHeader(BinaryReader file)
        {
            Header header = new Header(
                version: file.ReadInt32(),
                author: readString(file),
                description: readString(file),
                dependencies: GetSplit(file),
                referenced: readString(file).Split(','));
            return header;

            static string[] GetSplit(BinaryReader file)
            {
                var o = readString(file).Split(',');
                return o.Length == 1 && o[0] == "" ? new string[0] : o;
            }
        }

        public Item getItem(string id)
        {
            Item obj = (Item)null;
            return this.items.TryGetValue(id, out obj) ? obj : (Item)null;
        }

        public bool Load(string filename, ModMode mode)
        {
            if (!System.IO.File.Exists(filename))
                return false;
            string fileName = Path.GetFileName(filename);
            this.Signature = filename.SHA256CheckSum();
            this.Filename = filename;

            using (MemoryStream memoryStream = new MemoryStream())
            {
                try
                {
                    using (Stream stream = (Stream)System.IO.File.OpenRead(filename))
                        stream.CopyTo((Stream)memoryStream);
                }
                catch (Exception ex)
                {
                    return false;
                }
                memoryStream.Position = 0L;
                using (BinaryReader file = new BinaryReader((Stream)memoryStream))
                {
                    try
                    {
                        int fileVersion = file.ReadInt32();
                        if (fileVersion > 15)
                        {
                            Header header = readHeader(file);
                            if (mode == ModMode.ACTIVE)
                                this.header = header;
                        }

                        this.lastID = Math.Max(this.lastID, file.ReadInt32());
                        int MaxItemCount = file.ReadInt32();
                        this.items = new Dictionary<string, Item>(MaxItemCount);
                        for (int index = 0; index < MaxItemCount; ++index)
                        {
                            file.ReadInt32();
                            ItemType type = (ItemType)file.ReadInt32();
                            int num2 = file.ReadInt32();
                            string name = readString(file);
                            string str = fileVersion >= 7 ? readString(file) : num2.ToString() + "-" + fileName;
                            Item obj = this.getItem(str);
                            bool newItem = obj == null;
                            if (obj == null)
                            {
                                obj = new Item(type, str);
                                this.items.Add(str, obj);
                            }

                            int num3 = obj.Load(file, name, mode, fileVersion, fileName, newItem) ? 1 : 0;
                            if (obj.GetState() == State.REMOVED)
                            {
                                obj.RefreshState();
                                if (obj.GetState() == State.OWNED)
                                    this.items.Remove(obj.stringID);
                                else
                                    obj.flagDeleted();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could't Load the mod :(");
                        return false;
                    }
                }
            }
            return true;
        }

        public class Instance : Item
        {
            public Item resolvedRef;
            public ArrayList resolvedStates;

            public Instance()
              : base(ItemType.NULL_ITEM, "")
            {
                this.Name = "";
            }
        }

        public class Item
        {
            public void revert()
            {
                this.modData.Clear();
                if (this.baseName != null)
                    this.modName = (string)null;
                else
                    this.SetMissingValues();
                foreach (KeyValuePair<string, ArrayList> reference1 in this.references)
                {
                    List<Reference> referenceList = new List<Reference>();
                    foreach (Reference reference2 in reference1.Value)
                    {
                        if (reference2.original == null && reference2.locked == null)
                            referenceList.Add(reference2);
                        reference2.mod = (TripleInt)null;
                    }
                    foreach (Reference r in referenceList)
                        this.RemoveReference(reference1.Key, r);
                }
                foreach (KeyValuePair<string, ArrayList> keyValuePair in this.removed)
                {
                    foreach (Reference reference in keyValuePair.Value)
                    {
                        reference.mod = (TripleInt)null;
                        if (reference.item != null)
                            ++reference.item.refCount;
                        this.references[keyValuePair.Key].Add((object)reference);
                    }
                }
                this.removed.Clear();
                List<string> stringList = new List<string>(this.instances.Count);
                foreach (KeyValuePair<string, Instance> instance in this.instances)
                {
                    if (instance.Value.GetState() == State.OWNED)
                        stringList.Add(instance.Key);
                    else
                        instance.Value.revert();
                }
                foreach (string key in stringList)
                    this.instances.Remove(key);
                this.RefreshState();
            }

            public void flagDeleted()
            {
                this.revert();
                this.cachedState = State.REMOVED;
            }

            public class Accessor<T>
            {
                public Item item;

                public Accessor(Item me)
                {
                    this.item = me;
                }
                public T this[string s]
                {
                    get
                    {
                        return (T)this.item[s];
                    }
                    set
                    {
                        this.item[s] = (object)value;
                    }
                }
            }

            public SortedList<string, object> data = new SortedList<string, object>();
            public SortedList<string, object> modData = new SortedList<string, object>();
            public SortedList<string, object> lockedData = new SortedList<string, object>();
            public SortedList<string, ArrayList> references = new SortedList<string, ArrayList>();
            public SortedList<string, ArrayList> removed = new SortedList<string, ArrayList>();
            public SortedList<string, Instance> instances = new SortedList<string, Instance>();
            public int id;
            public State cachedState;
            public string baseName;
            public string modName;
            public string lockedName;
            public string mod;
            public Accessor<int> idata;
            public Accessor<bool> bdata;
            public Accessor<float> fdata;
            public Accessor<string> sdata;
            public int lastID;
            internal ChangeData ChangeData;

            public string stringID { get; set; }

            public ItemType type { get; private set; }

            public int refCount { get; private set; }

            public Item(ItemType type, string id)
            {
                this.type = type;
                this.stringID = id;
                this.setupAccessors();
                this.refCount = 0;
            }

            public string Name
            {
                get
                {
                    if (this.lockedName != null)
                        return this.lockedName;
                    return this.modName == null ? this.baseName : this.modName;
                }
                set
                {
                    this.modName = this.baseName == value ? (string)null : value;
                    this.RefreshState();
                }
            }

            public object this[string s]
            {
                get
                {
                    if (this.lockedData.ContainsKey(s))
                        return this.lockedData[s];
                    return !this.modData.ContainsKey(s) ? this.data[s] : this.modData[s];
                }
                set
                {
                    if (this.data.ContainsKey(s) && value.Equals(this.data[s]))
                    {
                        if (this.modData.ContainsKey(s))
                            this.modData.Remove(s);
                    }
                    else
                        this.modData[s] = value;
                    this.RefreshState();
                }
            }

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                foreach (KeyValuePair<string, object> keyValuePair in this.lockedData)
                    yield return keyValuePair;
                foreach (KeyValuePair<string, object> keyValuePair in this.modData)
                {
                    if (!this.lockedData.ContainsKey(keyValuePair.Key))
                        yield return keyValuePair;
                }
                foreach (KeyValuePair<string, object> keyValuePair in this.data)
                {
                    if (!this.modData.ContainsKey(keyValuePair.Key) && !this.lockedData.ContainsKey(keyValuePair.Key))
                        yield return keyValuePair;
                }
            }

            public bool ContainsKey(string key)
            {
                return this.modData.ContainsKey(key) || this.data.ContainsKey(key) || this.lockedData.ContainsKey(key);
            }

            public object OriginalValue(string key)
            {
                return !this.data.ContainsKey(key) ? (object)null : this.data[key];
            }

            public string OriginalName
            {
                get
                {
                    return this.baseName;
                }
            }

            public override string ToString()
            {
                return $"{this.type}: {this.Name}";
            }

            public void setupAccessors()
            {
                this.idata = new Accessor<int>(this);
                this.fdata = new Accessor<float>(this);
                this.bdata = new Accessor<bool>(this);
                this.sdata = new Accessor<string>(this);
            }

            public void addRef(Item from)
            {
                foreach (string referenceList in from.referenceLists())
                {
                    foreach (Reference reference in from.references[referenceList])
                    {
                        if (reference.item == this)
                            return;
                    }
                }
                foreach (Instance instance in (IEnumerable<Instance>)from.instances.Values)
                {
                    if (instance.resolvedRef == this)
                        return;
                    if (instance.resolvedStates != null)
                    {
                        foreach (Item resolvedState in instance.resolvedStates)
                        {
                            if (resolvedState == this)
                                return;
                        }
                    }
                }
                ++this.refCount;
            }

            public void removeRef(Item from)
            {
                bool flag = false;
                foreach (string referenceList in from.referenceLists())
                {
                    foreach (Reference reference in from.references[referenceList])
                    {
                        if (reference.item == this)
                        {
                            if (flag)
                                return;
                            flag = true;
                        }
                    }
                }
                foreach (Instance instance in (IEnumerable<Instance>)from.instances.Values)
                {
                    if (instance.resolvedRef == this)
                    {
                        if (flag)
                            return;
                        flag = true;
                    }
                    if (instance.resolvedStates != null)
                    {
                        foreach (Item resolvedState in instance.resolvedStates)
                        {
                            if (resolvedState == this)
                            {
                                if (flag)
                                    return;
                                flag = true;
                            }
                        }
                    }
                }
                --this.refCount;
            }

            public void removeRefTargets()
            {
                foreach (string referenceList in this.referenceLists())
                {
                    foreach (Reference reference in this.references[referenceList])
                    {
                        if (reference.item != null)
                            reference.item.removeRef(this);
                        reference.item = (Item)null;
                    }
                }
                foreach (Instance instance in (IEnumerable<Instance>)this.instances.Values)
                {
                    if (instance.resolvedRef != null)
                        instance.resolvedRef.removeRef(this);
                    if (instance.resolvedStates != null)
                    {
                        foreach (Item resolvedState in instance.resolvedStates)
                            resolvedState?.removeRef(this);
                    }
                }
            }

            public Reference addReference(
              string section,
              string id,
              int? v0 = null,
              int? v1 = null,
              int? v2 = null)
            {
                Reference reference = this.getReference(section, id);
                if (reference == null)
                {
                    reference = this.getRemovedReference(section, id);
                    if (reference != null)
                        this.removed[section].Remove((object)reference);
                    else
                        reference = new Reference(id, (TripleInt)null);
                    this.references[section].Add((object)reference);
                    Desc desc = getDesc(this.type, section);
                    reference.mod = !(desc.defaultValue is TripleInt) ? new TripleInt(0, 0, 0) : new TripleInt((TripleInt)desc.defaultValue);
                    if (v0.HasValue)
                        reference.mod.v0 = v0.Value;
                    if (v1.HasValue)
                        reference.mod.v1 = v1.Value;
                    if (v2.HasValue)
                        reference.mod.v2 = v2.Value;
                    if (reference.original != null && reference.original.Equals(reference.mod))
                        reference.mod = (TripleInt)null;
                    this.RefreshState();
                }
                return reference;
            }

            public Reference getReference(string section, string id)
            {
                if (!this.references.ContainsKey(section))
                    this.references.Add(section, new ArrayList());
                foreach (Reference reference in this.references[section])
                {
                    if (reference.itemID == id)
                        return reference;
                }
                return (Reference)null;
            }

            public bool hasReference(string section)
            {
                return this.references.ContainsKey(section) && this.references[section].Count > 0;
            }

            public Reference getRemovedReference(string section, string id)
            {
                if (this.removed.ContainsKey(section))
                {
                    foreach (Reference reference in this.removed[section])
                    {
                        if (reference.itemID == id)
                            return reference;
                    }
                }
                return (Reference)null;
            }

            public void RemoveReference(string section, string id)
            {
                this.RemoveReference(section, this.getReference(section, id));
            }

            public void RemoveReference(string section, Reference r)
            {
                if (r == null)
                    return;
                if (r.item != null)
                    r.item.removeRef(this);
                if (r.original != null)
                {
                    r.item = (Item)null;
                    r.mod = Reference.Removed;
                    if (!this.removed.ContainsKey(section))
                        this.removed.Add(section, new ArrayList());
                    this.removed[section].Add((object)r);
                }
                this.references[section].Remove((object)r);
                this.RefreshState();
            }

            public int GetReferenceCount(string section)
            {
                return !this.references.ContainsKey(section) ? 0 : this.references[section].Count;
            }

            public void ResolveReference(GameData source, string id, ref Item target)
            {
                Item obj = source.getItem(id);
                if (obj != null && target != obj)
                    obj.addRef(this);
                target = obj;
            }

            public void resolveReferences(GameData source)
            {
                foreach (KeyValuePair<string, ArrayList> reference1 in this.references)
                {
                    foreach (Reference reference2 in reference1.Value)
                        this.ResolveReference(source, reference2.itemID, ref reference2.item);
                }
                foreach (Instance instance in (IEnumerable<Instance>)this.instances.Values)
                {
                    if (instance.GetState() != State.REMOVED && instance.GetState() != State.LOCKED_REMOVED)
                    {
                        this.ResolveReference(source, instance.sdata["ref"], ref instance.resolvedRef);
                        int referenceCount = instance.GetReferenceCount("states");
                        if (referenceCount > 0)
                        {
                            if (instance.resolvedStates == null)
                                instance.resolvedStates = new ArrayList();
                            while (instance.resolvedStates.Count < referenceCount)
                                instance.resolvedStates.Add((object)null);
                            for (int index = 0; index < referenceCount; ++index)
                            {
                                Item resolvedState = instance.resolvedStates[index] as Item;
                                this.ResolveReference(source, (instance.references["states"][index] as Reference).itemID, ref resolvedState);
                                instance.resolvedStates[index] = (object)resolvedState;
                            }
                        }
                    }
                }
            }

            public int countChangedReferences()
            {
                int num = 0;
                foreach (ArrayList arrayList in (IEnumerable<ArrayList>)this.removed.Values)
                {
                    foreach (Reference reference in arrayList)
                    {
                        if (reference.locked == null)
                            ++num;
                    }
                }
                foreach (ArrayList arrayList in (IEnumerable<ArrayList>)this.references.Values)
                {
                    foreach (Reference reference in arrayList)
                    {
                        if (reference.mod != null)
                            ++num;
                    }
                }
                return num;
            }

            public IEnumerable<string> referenceLists()
            {
                foreach (KeyValuePair<string, ArrayList> reference in this.references)
                    yield return reference.Key;
            }

            public IEnumerable<KeyValuePair<string, TripleInt>> referenceData(
              string name,
              bool includeDeleted = false)
            {
                if (this.references.ContainsKey(name))
                {
                    foreach (Reference reference in this.references[name])
                        yield return new KeyValuePair<string, TripleInt>(reference.itemID, new TripleInt(reference.Values));
                }
                if (includeDeleted && this.removed.ContainsKey(name))
                {
                    foreach (Reference reference in this.removed[name])
                        yield return new KeyValuePair<string, TripleInt>(reference.itemID, new TripleInt(reference.Values));
                }
            }

            public Instance GetInstance(string id)
            {
                return !this.instances.ContainsKey(id) ? (Instance)null : this.instances[id];
            }

            public IEnumerable<KeyValuePair<string, Instance>> InstanceData()
            {
                foreach (KeyValuePair<string, Instance> instance in this.instances)
                    yield return instance;
            }

            public int countChangedInstances()
            {
                int num = 0;
                foreach (Instance instance in (IEnumerable<Instance>)this.instances.Values)
                {
                    if (instance.GetState() == State.MODIFIED || instance.GetState() == State.OWNED || instance.GetState() == State.REMOVED)
                        ++num;
                }
                return num;
            }

            public State GetState(string s)
            {
                if (this.lockedData.ContainsKey(s))
                    return State.LOCKED;
                if (this.modData.ContainsKey(s))
                    return State.MODIFIED;
                return this.data.ContainsKey(s) ? State.ORIGINAL : State.INVALID;
            }

            public State GetState(string section, string id)
            {
                Reference reference = this.getReference(section, id);
                if (reference == null)
                {
                    Reference removedReference = this.getRemovedReference(section, id);
                    if (removedReference == null)
                        return State.INVALID;
                    return removedReference.locked != null ? State.LOCKED_REMOVED : State.REMOVED;
                }

                if (reference.item == null || reference.item.GetState() == State.REMOVED)
                    return State.INVALID;
                if (reference.locked != null)
                    return State.LOCKED;
                if (reference.mod != null && reference.original != null)
                    return State.MODIFIED;
                return reference.original == null ? State.OWNED : State.ORIGINAL;
            }

            public State GetState()
            {
                if (this.cachedState == State.UNKNOWN)
                    this.cachedState = this.baseName != null ? (!this.HasLocalChanges() ? State.ORIGINAL : State.MODIFIED) : State.OWNED;
                return this.cachedState;
            }

            public bool HasLocalChanges()
            {
                return this.modName != null || this.modData.Count > 0 || this.countChangedReferences() > 0 || this.countChangedInstances() > 0;
            }

            public void RefreshState()
            {
                if (this.cachedState == State.LOCKED)
                    return;
                this.cachedState = State.UNKNOWN;
            }

            public int SetMissingValues()
            {
                int num = 0;
                if (desc.ContainsKey(this.type))
                {
                    foreach (KeyValuePair<string, Desc> keyValuePair in desc[this.type])
                    {
                        if (!(keyValuePair.Value.defaultValue is TripleInt) && !(keyValuePair.Value.defaultValue is Instance) && (keyValuePair.Value.defaultValue != null && !this.ContainsKey(keyValuePair.Key)))
                        {
                            this[keyValuePair.Key] = keyValuePair.Value.defaultValue;
                            ++num;
                        }
                    }
                }
                if (desc.ContainsKey(this.type))
                {
                    foreach (KeyValuePair<string, Desc> keyValuePair in desc[this.type])
                    {
                        if (!(keyValuePair.Value.defaultValue is TripleInt) && !(keyValuePair.Value.defaultValue is Instance) && (keyValuePair.Value.defaultValue != null && this[keyValuePair.Key].GetType() != keyValuePair.Value.defaultValue.GetType()) && (!(this[keyValuePair.Key] is int) || !keyValuePair.Value.defaultValue.GetType().IsEnum && !(keyValuePair.Value.defaultValue is Color)))
                        {
                            object defaultValue = keyValuePair.Value.defaultValue;
                            object obj = this[keyValuePair.Key];
                            if (obj is bool flag)
                                obj = (object)(flag ? 1 : 0);
                            if (defaultValue is string)
                                this[keyValuePair.Key] = (object)obj.ToString();
                            else if (defaultValue is float && obj.GetType().IsValueType)
                                this[keyValuePair.Key] = (object)(float)(int)obj;
                            else if (defaultValue is int || defaultValue is Color || defaultValue.GetType().IsEnum && obj is float)
                                this[keyValuePair.Key] = (object)(int)(float)obj;
                            else if (!(defaultValue is EnumValue) || !(obj is int))
                                this[keyValuePair.Key] = defaultValue;
                            else
                                continue;
                        }
                    }
                }

                return num;
            }

            public void setLocked()
            {
                if (this.cachedState == State.REMOVED)
                    this.cachedState = State.LOCKED_REMOVED;
                else
                    this.cachedState = State.LOCKED;
            }

            public bool tagged(Dictionary<string, bool> tags, string key)
            {
                if (tags == null)
                    return true;
                return tags.ContainsKey(key) && tags[key];
            }

            public bool Load(
              BinaryReader file,
              string name,
              ModMode mode,
              int fileVersion,
              string filename,
              bool newItem)
            {
                SortedList<string, object> sortedList = (SortedList<string, object>)null;
                switch (mode)
                {
                    case ModMode.BASE:
                        sortedList = this.data;
                        break;

                    case ModMode.ACTIVE:
                        sortedList = this.modData;
                        break;

                    case ModMode.LOCKED:
                        sortedList = this.lockedData;
                        break;
                }
                bool flag1 = false;
                Dictionary<string, bool> tags = null;
                if (fileVersion >= 15)
                {
                    ItemLoadFlags itemLoadFlags = (ItemLoadFlags)(file.ReadInt32() & int.MaxValue);
                    if (itemLoadFlags.HasFlag((Enum)ItemLoadFlags.MODIFIED) & newItem)
                    {
                        this.baseName = itemLoadFlags.HasFlag((Enum)ItemLoadFlags.RENAMED) ? "?" : name;
                    }
                    if (!itemLoadFlags.HasFlag((Enum)ItemLoadFlags.RENAMED) && this.Name != null)
                        name = this.Name;
                }
                else if (fileVersion >= 11)
                {
                    int num = file.ReadInt32();
                    if (num > 0 && filename != "gamedata.base")
                    {
                        tags = new Dictionary<string, bool>(num);
                        for (int index1 = 0; index1 < num; ++index1)
                        {
                            string index2 = readString(file);
                            tags[index2] = file.ReadBoolean();
                        }
                    }
                }
                if (mode == ModMode.BASE)
                    this.baseName = name;
                else if (name != this.Name)
                {
                    switch (mode)
                    {
                        case ModMode.ACTIVE:
                            this.modName = name;
                            break;

                        case ModMode.LOCKED:
                            this.lockedName = name;
                            break;
                    }
                }
                if (newItem)
                    this.mod = filename;
                int num1 = file.ReadInt32();
                for (int index = 0; index < num1; ++index)
                {
                    string key = readString(file);
                    bool flag2 = file.ReadBoolean();
                    if (this.tagged(tags, key))
                    {
                        sortedList[key] = (object)flag2;
                    }
                }
                int num2 = file.ReadInt32();
                for (int index = 0; index < num2; ++index)
                {
                    string key = readString(file);
                    float num3 = file.ReadSingle();
                    if (this.tagged(tags, key))
                    {
                        sortedList[key] = (object)num3;
                    }
                }
                int num4 = file.ReadInt32();
                for (int index = 0; index < num4; ++index)
                {
                    string key = readString(file);
                    int num3 = file.ReadInt32();
                    if (this.tagged(tags, key))
                    {
                        sortedList[key] = (object)num3;
                    }
                }
                if (fileVersion > 8)
                {
                    int num3 = file.ReadInt32();
                    for (int index = 0; index < num3; ++index)
                    {
                        string key = readString(file);
                        vec vec = new vec(x: file.ReadSingle(),
                            y: file.ReadSingle(),
                            z: file.ReadSingle());

                        if (this.tagged(tags, key))
                            sortedList[key] = vec;
                    }
                    int num5 = file.ReadInt32();
                    for (int index = 0; index < num5; ++index)
                    {
                        string key = readString(file);
                        quat quat = new quat(x: file.ReadSingle(), y: file.ReadSingle(), z: file.ReadSingle(), w: file.ReadSingle());

                        if (this.tagged(tags, key))
                            sortedList[key] = quat;
                    }
                }
                int num6 = file.ReadInt32();
                for (int index = 0; index < num6; ++index)
                {
                    string key = readString(file);
                    string str = readString(file);
                    if ((!sortedList.ContainsKey(key) || sortedList[key] is string) && this.tagged(tags, key))
                        sortedList[key] = str;
                }
                int num7 = file.ReadInt32();
                for (int index = 0; index < num7; ++index)
                {
                    string key = readString(file);
                    string f = readString(file);
                    if (this.tagged(tags, key))
                        sortedList[key] = f;
                }
                int num8 = file.ReadInt32();
                for (int index1 = 0; index1 < num8; ++index1)
                {
                    string section = readString(file);
                    int num3 = file.ReadInt32();
                    for (int index2 = 0; index2 < num3; ++index2)
                    {
                        if (fileVersion < 8)
                        {
                            file.ReadInt64();
                        }
                        else
                        {
                            string id = readString(file);
                            TripleInt tripleInt = new TripleInt(0, 0, 0);
                            tripleInt.v0 = file.ReadInt32();
                            if (fileVersion >= 10)
                            {
                                tripleInt.v1 = file.ReadInt32();
                                tripleInt.v2 = file.ReadInt32();
                            }
                            if (tags == null || tags.ContainsKey("-ref-" + id))
                            {
                                bool flag2 = tags != null && !tags["-ref-" + id] || tripleInt.v2 == int.MaxValue;
                                Reference reference = this.getReference(section, id);
                                if (!flag2 || reference != null)
                                {
                                    if (reference == null)
                                    {
                                        reference = new Reference(id, (TripleInt)null);
                                        this.references[section].Add((object)reference);
                                    }
                                    else if (flag2)
                                    {
                                        if (mode == ModMode.BASE)
                                        {
                                            if (reference.item != null)
                                                reference.item.removeRef(this);
                                            this.references[section].Remove((object)reference);
                                        }
                                        else
                                            this.RemoveReference(section, id);
                                        tripleInt = Reference.Removed;
                                    }
                                    if (mode == ModMode.ACTIVE && this.type == ItemType.DIALOGUE_PACKAGE && tripleInt.v1 == 100)
                                        tripleInt.v1 = 0;
                                    switch (mode)
                                    {
                                        case ModMode.BASE:
                                            reference.original = tripleInt;
                                            continue;
                                        case ModMode.ACTIVE:
                                            reference.mod = tripleInt;
                                            continue;
                                        case ModMode.LOCKED:
                                            reference.locked = tripleInt;
                                            continue;
                                        default:
                                            continue;
                                    }
                                }
                            }
                        }
                    }
                }
                int num9 = file.ReadInt32();
                for (int index1 = 0; index1 < num9; ++index1)
                {
                    int num3;
                    string str;
                    if (fileVersion >= 15)
                    {
                        str = readString(file);
                    }
                    else
                    {
                        num3 = file.ReadInt32();
                        str = num3.ToString() + "-" + filename;
                    }
                    string index2 = str;
                    Instance instance1 = this.GetInstance(index2) ?? new Instance();
                    instance1["ref"] = fileVersion < 8 ? (object)"" : (object)readString(file);
                    instance1["x"] = (object)file.ReadSingle();
                    instance1["y"] = (object)file.ReadSingle();
                    instance1["z"] = (object)file.ReadSingle();
                    instance1["qw"] = (object)file.ReadSingle();
                    instance1["qx"] = (object)file.ReadSingle();
                    instance1["qy"] = (object)file.ReadSingle();
                    instance1["qz"] = (object)file.ReadSingle();
                    if (fileVersion > 6)
                    {
                        int num5 = file.ReadInt32();
                        if (fileVersion < 15)
                        {
                            for (int index3 = 0; index3 < num5; ++index3)
                            {
                                Instance instance2 = instance1;
                                num3 = file.ReadInt32();
                                string id = num3.ToString() + "-" + filename + "-INGAME";
                                int? v0 = new int?();
                                int? v1 = new int?();
                                int? v2 = new int?();
                                instance2.addReference("states", id, v0, v1, v2);
                            }
                        }
                        else
                        {
                            for (int index3 = 0; index3 < num5; ++index3)
                                instance1.addReference("states", readString(file), new int?(), new int?(), new int?());
                        }
                    }

                    if (!this.instances.ContainsKey(index2))
                        this.instances.Add(index2, instance1);
                    if (mode == ModMode.LOCKED)
                        instance1.setLocked();
                }
                if (this.ContainsKey("REMOVED"))
                {
                    if (this.bdata["REMOVED"])
                        this.cachedState = State.REMOVED;
                    if (mode == ModMode.LOCKED)
                        this.setLocked();
                    sortedList.Remove("REMOVED");
                    this.modData.Remove("REMOVED");
                    this.lockedData.Remove("REMOVED");
                    this.removeRefTargets();
                }
                else if (mode == ModMode.BASE)
                    this.cachedState = State.ORIGINAL;
                else if (newItem && mode == ModMode.LOCKED)
                    this.setLocked();
                else
                    this.RefreshState();
                if (!flag1 & newItem && mode == ModMode.ACTIVE && (true && this.SetMissingValues() > 0))
                    if (mode == ModMode.ACTIVE && this.baseName != null)
                    {
                        foreach (KeyValuePair<string, object> keyValuePair in this.data)
                        {
                            if (this.modData.ContainsKey(keyValuePair.Key) && this.modData[keyValuePair.Key].Equals(keyValuePair.Value))
                                this.modData.Remove(keyValuePair.Key);
                        }
                        foreach (KeyValuePair<string, ArrayList> reference1 in this.references)
                        {
                            foreach (Reference reference2 in reference1.Value)
                            {
                                if (reference2.original != null && reference2.original.Equals(reference2.mod))
                                    reference2.mod = (TripleInt)null;
                            }
                        }
                    }
                return !flag1;
            }

        }
    }
}