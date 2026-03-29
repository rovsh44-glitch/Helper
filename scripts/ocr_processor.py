import easyocr
import sys
import os
import fitz  # PyMuPDF
from PIL import Image
import io

def process_pdf(pdf_path):
    print(f"[*] Starting OCR for: {pdf_path}")
    print("[*] Mode: STABLE CPU (PyMuPDF fallback)")
    
    # Initialize reader
    reader = easyocr.Reader(['ru', 'en'], gpu=False)
    
    try:
        # Open PDF via PyMuPDF
        doc = fitz.open(pdf_path)
        full_text = []
        
        print(f"[*] Total pages: {len(doc)}")
        
        for i in range(len(doc)):
            page = doc.load_page(i)
            print(f"[*] Processing page {i+1}/{len(doc)}...")
            
            # Render page to image
            pix = page.get_pixmap(dpi=200)
            img_data = pix.tobytes("jpg")
            
            # OCR the image from memory
            result = reader.readtext(img_data, detail=0)
            full_text.append(" ".join(result))
            
        return "\n\n".join(full_text)
    except Exception as e:
        return f"Error during OCR: {str(e)}"

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python ocr_processor.py <path_to_pdf>")
        sys.exit(1)
        
    path = sys.argv[1]
    text = process_pdf(path)
    
    output_path = path + ".ocr.txt"
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(text)
    
    print(f"[+] OCR Complete. Saved to: {output_path}")
