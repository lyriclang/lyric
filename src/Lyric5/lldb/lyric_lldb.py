"""Lyric's values in lldb: strings as text, arrays by their elements, optionals and enums by what
they hold, a struct's fields by their Lyric names. The types come from the names the build writes
beside the binary, <binary>.lyric-names; lyric dap imports this file, and lldb does with

    command script import <toolchain>/lldb/lyric_lldb.py
"""
import lldb

_tables = {}

# the Lyric names of the C scalars an array's elements can be
_SCALARS = {
    "int": "int64_t", "uint": "uint64_t", "int8": "int8_t", "int16": "int16_t", "int32": "int32_t",
    "uint8": "uint8_t", "uint16": "uint16_t", "uint32": "uint32_t", "float": "double", "float32": "float",
    "bool": "uint8_t", "char": "uint32_t", "string": "LyrStr *",
}

_MOST = 512


def _table(target):
    """The binary's names: types by C name, C names by Lyric name, variants by enum and tag."""
    exe = target.GetExecutable().fullpath if target else None
    if exe in _tables:
        return _tables[exe]
    types, back, variants = {}, {}, {}
    try:
        with open(exe + ".lyric-names", encoding="utf-8") as names:
            for line in names:
                parts = line.rstrip("\n").split("\t")
                if parts[0] == "type" and len(parts) == 3:
                    types[parts[1]] = parts[2]
                    back[parts[2]] = parts[1]
                elif parts[0] == "variant" and len(parts) == 5:
                    variants.setdefault(parts[1], {})[int(parts[2])] = (parts[3], parts[4] == "1")
    except (OSError, TypeError):
        pass
    _tables[exe] = (types, back, variants)
    return _tables[exe]


def _object(valobj):
    """The value a reference points at, the value itself otherwise; None for a null reference."""
    if valobj.TypeIsPointerType():
        if valobj.GetValueAsUnsigned() == 0:
            return None
        return valobj.Dereference()
    return valobj


def _c_string(valobj):
    address = valobj.GetValueAsUnsigned()
    if address == 0:
        return ""
    error = lldb.SBError()
    text = valobj.GetProcess().ReadCStringFromMemory(address, 256, error)
    return text if error.Success() else ""


def _base(name):
    name = name.strip()
    for prefix in ("const ", "struct "):
        if name.startswith(prefix):
            name = name[len(prefix):]
    return name.rstrip(" *")


class StringProvider:
    """A string has its text and no parts."""

    def __init__(self, valobj, internal_dict):
        pass

    def update(self):
        return False

    def num_children(self):
        return 0

    def has_children(self):
        return False


def string_summary(valobj, internal_dict):
    obj = _object(valobj.GetNonSyntheticValue())
    if obj is None:
        return "null"
    # the string's own parts, not the none the provider gives
    obj = obj.GetNonSyntheticValue()
    length = obj.GetChildMemberWithName("len").GetValueAsSigned()
    address = obj.GetChildMemberWithName("bytes").GetLoadAddress()
    data = b""
    if length > 0:
        error = lldb.SBError()
        data = obj.GetProcess().ReadMemory(address, min(length, _MOST), error) or b""
    text = data.decode("utf-8", "replace").replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")
    return '"' + text + ('…' if length > _MOST else '') + '"'


class ArrayProvider:
    """An array's elements, typed by the element its descriptor names."""

    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.count = 0

    def update(self):
        self.count = 0
        obj = _object(self.valobj)
        if obj is None:
            return False
        self.count = obj.GetChildMemberWithName("len").GetValueAsSigned()
        desc = obj.GetChildMemberWithName("header").GetChildMemberWithName("desc")
        self.size = desc.Dereference().GetChildMemberWithName("elem_size").GetValueAsUnsigned()
        name = _c_string(desc.Dereference().GetChildMemberWithName("name"))
        element = name[:-2] if name.endswith("[]") else ""
        _, back, _ = _table(self.valobj.GetTarget())
        c = _SCALARS.get(element) or back.get(element)
        target = self.valobj.GetTarget()
        self.type = target.FindFirstType(_base(c)) if c else None
        if self.type and self.type.IsValid():
            if c.endswith("*") or (self.type.GetByteSize() != self.size and self.size == 8):
                self.type = self.type.GetPointerType()
        else:
            self.type = target.FindFirstType("uint8_t")
        self.data = obj.GetChildMemberWithName("data").GetLoadAddress()
        return False

    def num_children(self):
        return max(0, min(self.count, 1000))

    def get_child_index(self, name):
        try:
            return int(name.lstrip("[").rstrip("]"))
        except ValueError:
            return -1

    def get_child_at_index(self, index):
        return self.valobj.CreateValueFromAddress("[%d]" % index, self.data + index * self.size, self.type)

    def has_children(self):
        return True


def array_summary(valobj, internal_dict):
    obj = _object(valobj.GetNonSyntheticValue())
    if obj is None:
        return "null"
    return "[%d]" % obj.GetChildMemberWithName("len").GetValueAsSigned()


class OptionalProvider:
    """An optional: its value where it has one, nothing where it has none."""

    def __init__(self, valobj, internal_dict):
        self.valobj = valobj

    def update(self):
        self.has = self.valobj.GetChildMemberWithName("has").GetValueAsUnsigned() != 0
        return False

    def num_children(self):
        return 1 if self.has else 0

    def get_child_index(self, name):
        return 0 if name == "value" else -1

    def get_child_at_index(self, index):
        return self.valobj.GetChildMemberWithName("value") if self.has and index == 0 else None


def optional_summary(valobj, internal_dict):
    raw = valobj.GetNonSyntheticValue()
    if raw.GetChildMemberWithName("has").GetValueAsUnsigned() == 0:
        return "none"
    value = raw.GetChildMemberWithName("value")
    return value.GetSummary() or value.GetValue() or ""


class StructProvider:
    """A struct's or a class's fields by their Lyric names, the header left out; an enum's payload
    under its variant."""

    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.children = []

    def update(self):
        self.children = []
        obj = _object(self.valobj)
        if obj is None:
            return False
        _, _, variants = _table(self.valobj.GetTarget())
        name = _base(obj.GetType().GetName())
        if name in variants:
            tag = obj.GetChildMemberWithName("tag").GetValueAsUnsigned()
            variant = variants[name].get(tag)
            if variant and variant[1]:
                payload = obj.GetChildMemberWithName("as").GetChildMemberWithName("v%d" % tag)
                self.children = [payload.GetChildAtIndex(i) for i in range(payload.GetNumChildren())]
        else:
            self.children = [obj.GetChildAtIndex(i) for i in range(obj.GetNumChildren())]
        self.children = [c for c in self.children if c.GetName() not in ("header", "lyr_unit")]
        return False

    def num_children(self):
        return len(self.children)

    def get_child_index(self, name):
        for i, child in enumerate(self.children):
            if _field(child.GetName()) == name:
                return i
        return -1

    def get_child_at_index(self, index):
        child = self.children[index]
        return child.Clone(_field(child.GetName()))


def _field(name):
    return name[2:] if name and name.startswith("f_") else name


def struct_summary(valobj, internal_dict):
    obj = _object(valobj.GetNonSyntheticValue())
    if obj is None:
        return "null"
    types, _, variants = _table(valobj.GetTarget())
    name = _base(obj.GetType().GetName())
    if name in variants:
        tag = obj.GetChildMemberWithName("tag").GetValueAsUnsigned()
        variant = variants[name].get(tag)
        return variant[0] if variant else "tag %d" % tag
    return types.get(name, name)


def __lldb_init_module(debugger, internal_dict):
    module = __name__
    for command in (
        'type summary add -w lyric -e -F %s.string_summary -x "^(const )?(struct )?LyrStr( \\*)?$"' % module,
        'type synthetic add -w lyric -l %s.StringProvider -x "^(const )?(struct )?LyrStr( \\*)?$"' % module,
        'type synthetic add -w lyric -l %s.ArrayProvider -x "^(struct )?LyrArr \\*$"' % module,
        'type summary add -w lyric -e -F %s.array_summary -x "^(struct )?LyrArr \\*$"' % module,
        'type synthetic add -w lyric -l %s.OptionalProvider -x "^lyr_opt_"' % module,
        'type summary add -w lyric -e -F %s.optional_summary -x "^lyr_opt_"' % module,
        'type synthetic add -w lyric -l %s.StructProvider -x "^(struct )?lyr_ty_[A-Za-z0-9_]+( \\*)?$"' % module,
        'type summary add -w lyric -e -F %s.struct_summary -x "^(struct )?lyr_ty_[A-Za-z0-9_]+( \\*)?$"' % module,
        'type category enable lyric',
    ):
        debugger.HandleCommand(command)
