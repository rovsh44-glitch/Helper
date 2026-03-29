import os
import sys
import zipfile
import re
from html import unescape

def clean_html(html):
    # Remove script and style elements
    html = re.sub(r'<(script|style).*?>.*?</\1>', '', html, flags=re.DOTALL | re.IGNORECASE)
    # Remove tags
    text = re.sub(r'<.*?>', '', html, flags=re.DOTALL)
    # Unescape entities
    return unescape(text).strip()

def split_epub(file_path, output_dir):
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)
    
    print(f"Opening {file_path}...")
    with zipfile.ZipFile(file_path, 'r') as z:
        count = 0
        for name in z.namelist():
            if name.endswith(('.html', '.xhtml', '.htm')):
                with z.open(name) as f:
                    content = f.read().decode('utf-8', errors='ignore')
                    text = clean_html(content)
                    if len(text) > 100:
                        count += 1
                        file_name = f"chapter_{count:05d}.txt"
                        with open(os.path.join(output_dir, file_name), 'w', encoding='utf-8') as out:
                            out.write(text)
                        if count % 100 == 0:
                            print(f"Extracted {count} chapters...")
    
    print(f"Done! Extracted {count} chapters to {output_dir}")

def resolve_library_root():
    explicit_root = os.environ.get("HELPER_LIBRARY_ROOT")
    if explicit_root:
        return os.path.abspath(explicit_root)

    data_root = os.environ.get("HELPER_DATA_ROOT")
    if data_root:
        return os.path.abspath(os.path.join(data_root, "library"))

    script_root = os.path.abspath(os.path.dirname(__file__))
    helper_root = os.path.abspath(os.path.join(script_root, ".."))
    default_data_root = os.path.abspath(os.path.join(os.path.dirname(helper_root), "HELPER_DATA"))
    return os.path.join(default_data_root, "library")

if __name__ == "__main__":
    library_root = resolve_library_root()
    input_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(library_root, "300dpi.epub")
    output_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(library_root, "extracted_encyclopedia")
    split_epub(input_path, output_path)
