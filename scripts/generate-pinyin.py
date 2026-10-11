from pathlib import Path
from zipfile import ZipFile
import unicodedata

def normalize(reading):
    result=[]
    for char in unicodedata.normalize('NFD',reading.lower()):
        if char=='\u0308' and result and result[-1]=='u':result[-1]='v'
        elif unicodedata.category(char)!='Mn':result.append(char)
    return ''.join(result)

with ZipFile('publish/tools/Unihan.zip') as archive:
    lines=archive.read('Unihan_Readings.txt').decode('utf-8').splitlines()
    records=[]
    for line in lines:
        if not line or line.startswith('#'):continue
        code,prop,reading=line.split('\t',2)
        if prop=='kMandarin':records.append(code[2:]+'\t'+normalize(reading.split()[0]))
    Path('src/assets/pinyin.tsv').write_text('\n'.join(records)+'\n',encoding='utf-8')
license_text=Path('publish/tools/unicode-license.txt').read_text(encoding='utf-8-sig')
Path('THIRD_PARTY_NOTICES.md').write_text('''# Third-party data

`src/assets/pinyin.tsv` is derived from the Unicode 17.0.0 Unihan database,
specifically `kMandarin` readings. Tone marks are removed, with ü represented as v.
Source: https://www.unicode.org/Public/17.0.0/ucd/Unihan.zip
Regenerate with the downloaded Unihan archive and `scripts/generate-pinyin.py`.
Single-character readings use the database default; NameService contains a small
set of phrase overrides for common polyphonic names.

## Unicode license

'''+license_text,encoding='utf-8')
print(f'Embedded {len(records)} Mandarin readings.')
