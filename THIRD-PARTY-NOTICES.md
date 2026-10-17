# Third-party components

Snapview-owned code, documentation and original assets use the [MIT License](LICENSE).
The components below retain their own licenses and copyrights; the MIT grant does
not relicense them. See [docs/licensing.md](docs/licensing.md) for scope and
redistribution notes.

- **.NET / WPF**, Microsoft and contributors — upstream source uses MIT. [License](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT). Microsoft-distributed binaries carry applicable distribution terms and third-party notices. The copied `licenses/dotnet-LICENSE.txt` is Microsoft SDK distribution terms, not the upstream MIT source license.
- **Magick.NET**, Dirk Lemstra and contributors — Apache-2.0. [License](https://github.com/dlemstra/Magick.NET/blob/main/License.txt). ImageMagick and native codec/delegate libraries have separate terms. Preserve the full `licenses/Magick.NET-NOTICE.txt` from the NuGet package, including its LGPL/MPL component entries; review the exact binary's source/linkage obligations before redistribution.
- **MetadataExtractor**, Drew Noakes and contributors — Apache-2.0. [License](https://github.com/drewnoakes/metadata-extractor-dotnet/blob/main/LICENSE). Uses **XmpCore**, Adobe and contributors — BSD-3-Clause.
- **Tesseract .NET wrapper**, Charles Weld and contributors — Apache-2.0. [Project](https://github.com/charlesw/tesseract). License terms are included in `licenses/Apache-2.0.txt`.
- **Tesseract OCR**, Google and contributors — Apache-2.0. [License](https://github.com/tesseract-ocr/tesseract/blob/main/LICENSE). Uses Leptonica — BSD-2-Clause.
- **English OCR trained model**, tesseract-ocr/tessdata_fast contributors — Apache-2.0. [Source and license](https://github.com/tesseract-ocr/tessdata_fast). Bundled file SHA-256: `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`.

The English model is bundled for offline recognition. Setup only downloads it if missing. App runtime does not download models. The native Tesseract wrapper may require the Microsoft Visual C++ 2015–2022 x64 runtime on machines that do not already have it; use Microsoft's official redistributable if OCR reports a native loader error.
