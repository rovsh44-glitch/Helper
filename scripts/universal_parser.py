import sys
import os
import json
from pathlib import Path

def parse_file(file_path):
    ext = os.path.splitext(file_path)[1].lower()
    
    try:
        # We prefer 'unstructured' for high-quality RAG-ready output
        from unstructured.partition.auto import partition
        
        elements = partition(filename=file_path)
        # Convert elements to markdown-like text
        text = "\n\n".join([str(el) for l, el in enumerate(elements)])
        return {
            "status": "success",
            "content": text,
            "metadata": {"method": "unstructured", "extension": ext}
        }
    except ImportError:
        # Fallback to simple parsers if unstructured is not installed
        return fallback_parse(file_path, ext)
    except Exception as e:
        return {"status": "error", "message": str(e)}

def fallback_parse(file_path, ext):
    try:
        if ext == ".txt" or ext == ".md":
            with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                return {"status": "success", "content": f.read(), "metadata": {"method": "raw"}}
        
        if ext == ".pdf":
            import pypdf
            reader = pypdf.PdfReader(file_path)
            text = ""
            for page in reader.pages:
                text += page.extract_text() + "\n"
            return {"status": "success", "content": text, "metadata": {"method": "pypdf"}}

        if ext == ".epub":
            import ebooklib
            from ebooklib import epub
            from bs4 import BeautifulSoup
            book = epub.read_epub(file_path)
            chapters = []
            for item in book.get_items():
                if item.get_type() == ebooklib.ITEM_DOCUMENT:
                    soup = BeautifulSoup(item.get_content(), 'html.parser')
                    chapters.append(soup.get_text())
            return {"status": "success", "content": "\n\n".join(chapters), "metadata": {"method": "ebooklib"}}

        return {"status": "error", "message": f"No fallback parser for extension {ext}"}
    except Exception as e:
        return {"status": "error", "message": f"Fallback failed: {str(e)}"}

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"status": "error", "message": "No file path provided"}))
        sys.exit(1)
    
    target_path = sys.argv[1]
    if not os.path.exists(target_path):
        print(json.dumps({"status": "error", "message": "File not found"}))
        sys.exit(1)
        
    result = parse_file(target_path)
    print(json.dumps(result, ensure_ascii=False))
