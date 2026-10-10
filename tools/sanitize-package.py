"""Remove machine-specific debug metadata from a compiled tModLoader package.

Uses the official TmodFile / BuildProperties formats. Does not recompile or
change the assembly, textures, localization, dependencies, or gameplay.
"""
from pathlib import Path
import argparse, hashlib, io, struct, zlib

def read_string(stream):
    length = shift = 0
    while True:
        byte = stream.read(1)
        if not byte: raise ValueError('Truncated string')
        byte = byte[0]; length |= (byte & 127) << shift
        if byte < 128: break
        shift += 7
        if shift > 28: raise ValueError('Invalid string length')
    value = stream.read(length)
    if len(value) != length: raise ValueError('Truncated string payload')
    return value.decode('utf-8')

def write_string(value):
    value = value.encode('utf-8'); length = len(value); prefix = bytearray()
    while length >= 128:
        prefix.append((length & 127) | 128); length >>= 7
    return bytes(prefix) + bytes([length]) + value

def unpack(blob):
    stream = io.BytesIO(blob)
    assert stream.read(4) == b'TMOD'
    loader = read_string(stream); digest = stream.read(20); stream.read(256)
    length, = struct.unpack('<i', stream.read(4)); start = stream.tell()
    assert len(blob) - start == length
    assert hashlib.sha1(blob[start:]).digest() == digest
    name, version = read_string(stream), read_string(stream)
    count, = struct.unpack('<i', stream.read(4))
    table = [(read_string(stream), *struct.unpack('<ii', stream.read(8))) for _ in range(count)]
    resources = {}
    for path, size, packed in table:
        data = stream.read(packed)
        assert len(data) == packed
        if size != packed: data = zlib.decompress(data, -15)
        assert len(data) == size and path not in resources
        resources[path] = data
    assert stream.tell() == len(blob)
    return loader, name, version, resources

def clean_info(data, description=None):
    stream = io.BytesIO(data); result = bytearray()
    string_tags = {'author','version','displayName','homepage','description','eacPath','modSource','buildVersion'}
    list_tags = {'dllReferences','modReferences','weakReferences','sortAfter','sortBefore'}
    flags = {'noCompile','!playableOnPreview','translationMod','!hideCode','!hideResources','includeSource'}
    while True:
        start = stream.tell(); tag = read_string(stream)
        if not tag: break
        if tag in string_tags: read_string(stream)
        elif tag in list_tags:
            while read_string(stream): pass
        elif tag == 'side': assert len(stream.read(1)) == 1
        elif tag not in flags: raise ValueError('Unknown Info tag: ' + tag)
        if tag in {'modSource','eacPath'}: continue
        if tag == 'description' and description is not None:
            result += write_string(tag) + write_string(description)
        else: result += data[start:stream.tell()]
    assert stream.tell() == len(data)
    return bytes(result) + write_string('')

def pack(loader, name, version, resources):
    table = bytearray(); payloads = bytearray()
    for path, data in resources.items():
        packed = data
        if len(data) > 1024 and not path.endswith(('.png','.ogg','.mp3')):
            codec = zlib.compressobj(6, zlib.DEFLATED, -15)
            candidate = codec.compress(data) + codec.flush()
            if len(candidate) < len(data) * .9: packed = candidate
        table += write_string(path) + struct.pack('<ii',len(data),len(packed))
        payloads += packed
    payload = write_string(name) + write_string(version) + struct.pack('<i',len(resources)) + table + payloads
    return b'TMOD' + write_string(loader) + hashlib.sha1(payload).digest() + bytes(256) + struct.pack('<i',len(payload)) + payload

def sanitize(input_path, output_path, source=None):
    loader, name, version, old = unpack(input_path.read_bytes())
    resources = {k:v for k,v in old.items() if not k.lower().endswith('.pdb')}
    description = (source/'description.txt').read_text('utf-8') if source else None
    resources['Info'] = clean_info(old['Info'],description)
    if source:
        for path in ('description.txt','description_workshop.txt','changelog.txt'):
            if path in resources: resources[path] = (source/path).read_bytes()
    allowed = {'Info','description.txt','description_workshop.txt','changelog.txt'}
    assert all(v == old[k] for k,v in resources.items() if k not in allowed)
    assert old[name+'.dll'] == resources[name+'.dll']
    output_path.parent.mkdir(parents=True,exist_ok=True)
    output_path.write_bytes(pack(loader,name,version,resources))
    assert unpack(output_path.read_bytes())[3] == resources
    return resources

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('input',type=Path); parser.add_argument('output',type=Path)
    parser.add_argument('--source',type=Path)
    args = parser.parse_args()
    sanitize(args.input,args.output,args.source)
    print('Public package SHA256:',hashlib.sha256(args.output.read_bytes()).hexdigest().upper())
