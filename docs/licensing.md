# Licensing and third-party distribution

## Snapview's license

Snapview-owned source code, scripts, documentation and original project assets
are licensed under the [MIT License](../LICENSE), copyright 2026 Snapview
contributors. MIT permits use, modification, forking, distribution, sublicensing
and sale, including in commercial products. Copies/substantial portions must retain
the copyright and license notice. There is no requirement in MIT to publish a
fork's source code. The canonical [MIT text](https://opensource.org/license/mit)
defines those terms and the warranty disclaimer.

This choice applies to work owned by the Snapview contributors. It does **not**
relicense dependencies, their native libraries, the OCR model or test fixtures.
The top-level license is deliberately the standard MIT text; component exceptions
and redistribution details belong in this document and the notices.

## Component inventory

Direct package versions are pinned in `Snapview.csproj`. The following is an
overview, not a replacement for the accompanying license texts or a complete
native-library inventory:

| Component | Terms / notices to retain |
| --- | --- |
| Snapview-owned files | Root `LICENSE` (MIT) |
| .NET / WPF upstream source | MIT; upstream [runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and WPF notices |
| Microsoft-distributed SDK/runtime binaries | Applicable Microsoft distribution terms and third-party notices; see `licenses/dotnet-LICENSE.txt`, `dotnet-ThirdPartyNotices.txt`, `WPF-THIRD-PARTY-NOTICES.txt` |
| Magick.NET-Q8-x64 14.16.0 | Apache-2.0 wrapper; `licenses/Magick.NET-LICENSE.txt` |
| ImageMagick and bundled native codec/delegate components | ImageMagick and component-specific terms; full package inventory in `licenses/Magick.NET-NOTICE.txt` |
| MetadataExtractor 2.9.3 | Apache-2.0; `licenses/MetadataExtractor-LICENSE.txt` |
| XmpCore (transitive) | Adobe BSD terms; `licenses/XmpCore-LICENSE.txt` |
| Tesseract .NET wrapper 5.2.0 | Apache-2.0; `licenses/Tesseract-wrapper-LICENSE.txt` |
| Native Tesseract OCR / Leptonica | Apache-2.0 / BSD-2-Clause respectively; Apache and Leptonica texts in `licenses/` |
| English `tessdata_fast` model | Apache-2.0; provenance and SHA-256 in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) |
| Optional HEIC/AVIF test samples | Downloaded from the libheif examples into ignored `.tools/`; not part of the shipped app or covered by Snapview's MIT license |

The copied `dotnet-LICENSE.txt` came from a Microsoft SDK distribution. It is not
the upstream runtime's MIT source license and should not be described as one.
Keep track of the actual runtime/native packages included in each release rather
than assuming one license label covers the complete self-contained folder.

## Native codec obligations

Magick.NET's wrapper license does not describe every native component's terms.
The retained package notice includes LGPL entries such as **libheif**, **libde265**
and other delegates, plus MPL terms for **cairo**. The inventory can cover multiple
build configurations; determine which components are present/enabled in the exact
Windows binaries you distribute.

These terms can carry source availability, modification notices and/or recipient
replacement/relinking requirements, depending on the component and linkage. Adding
an MIT license to Snapview or copying a notice file alone does not discharge those
requirements. In particular, replacing a native wrapper DLL is not automatically
the same as allowing replacement of an LGPL library statically linked inside it.
See the [LGPL v3 combined-work terms](https://www.gnu.org/licenses/lgpl-3.0.html)
and the exact license sections in the bundled notice.

MIT is suitable for Snapview's own code while these components keep their original
terms. A binary distributor, including someone selling a fork, must meet the terms
of the dependencies actually shipped. Commercial permission for Snapview is not a
blanket certification that every possible native packaging/linkage choice complies.

## Maintaining a release

1. Keep the root MIT license, third-party summary and full `licenses/` texts in
   source and binary distributions. The project/publish script copy these files.
2. Record direct/transitive package versions and the native component versions
   from the exact NuGet artifacts used. Inspect `obj/project.assets.json` and the
   package's native notice/build provenance when dependencies change.
3. Preserve upstream copyright, license and NOTICE files. For modified Apache
   components retain the required notices and identify modifications as specified
   by [Apache-2.0 section 4](https://www.apache.org/licenses/LICENSE-2.0).
4. For shipped LGPL/MPL components, arrange the corresponding source, build or
   relinking materials and distribution method required by their actual terms.
   Upstream starting points include [Magick.Native](https://github.com/dlemstra/Magick.Native)
   and [ImageMagick's Windows dependencies](https://github.com/ImageMagick/Windows).
   A link to a moving upstream branch is not proof that matching source and
   build/relink materials are being supplied as required.
5. Check licensing/provenance for new fonts, icons, OCR models and fixtures too.
   Do not relabel imported assets MIT or bundle the downloaded test samples by
   accident. Update this inventory and `THIRD-PARTY-NOTICES.md` with changes.

Current publishing automates file copying, not a dependency license audit or
collection of corresponding native source/relink materials. Resolve those release
obligations before public binary distribution; changing Snapview's own license
does not require replacing its architecture or changing dependency licenses.
