use std::{collections::HashSet, path::Path};

use image::{imageops, DynamicImage, GrayImage, ImageBuffer, Luma};
use rxing::{
    common::HybridBinarizer, BarcodeFormat, BinaryBitmap, DecodeHints, Luma8LuminanceSource,
    MultiFormatReader, Reader,
};
use thiserror::Error;

#[derive(Debug, Error)]
pub enum BarcodeScanError {
    #[error("failed to open scan image {path}: {source}")]
    OpenImage {
        path: String,
        source: image::ImageError,
    },
    #[error("scan image is too large to decode safely")]
    ImageTooLarge,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BarcodeScanResult {
    pub text: String,
    pub format: String,
}

pub fn decode_barcode_image(
    path: impl AsRef<Path>,
) -> Result<Option<BarcodeScanResult>, BarcodeScanError> {
    let path = path.as_ref();
    let image = image::open(path).map_err(|source| BarcodeScanError::OpenImage {
        path: path.display().to_string(),
        source,
    })?;

    decode_barcode_dynamic_image(&image)
}

fn decode_barcode_dynamic_image(
    image: &DynamicImage,
) -> Result<Option<BarcodeScanResult>, BarcodeScanError> {
    let gray = image.to_luma8();
    if let Some(result) = try_decode_gray(&gray)? {
        return Ok(Some(result));
    }

    if let Some(band) = middle_band(&gray) {
        if let Some(result) = try_decode_gray(&band)? {
            return Ok(Some(result));
        }
    }

    let rotated = imageops::rotate90(&gray);
    if let Some(result) = try_decode_gray(&rotated)? {
        return Ok(Some(result));
    }

    if let Some(rotated_band) = middle_band(&rotated) {
        if let Some(result) = try_decode_gray(&rotated_band)? {
            return Ok(Some(result));
        }
    }

    let threshold = threshold_gray(&gray, 150);
    if let Some(result) = try_decode_gray(&threshold)? {
        return Ok(Some(result));
    }

    let scaled = scale_gray_nearest_2x(&gray)?;
    if let Some(result) = try_decode_gray(&scaled)? {
        return Ok(Some(result));
    }

    let scaled_threshold = threshold_gray(&scaled, 150);
    try_decode_gray(&scaled_threshold)
}

fn try_decode_gray(image: &GrayImage) -> Result<Option<BarcodeScanResult>, BarcodeScanError> {
    let width = image.width();
    let height = image.height();
    let pixels = checked_pixel_len(width, height)?;
    let mut source = Vec::with_capacity(pixels);
    source.extend_from_slice(image.as_raw());

    let hints = DecodeHints {
        PossibleFormats: Some(legacy_scan_formats()),
        TryHarder: Some(true),
        AlsoInverted: Some(true),
        ..DecodeHints::default()
    };
    let mut reader = MultiFormatReader::default();
    let mut bitmap = BinaryBitmap::new(HybridBinarizer::new(Luma8LuminanceSource::new(
        source, width, height,
    )));

    let Ok(result) = reader.decode_with_hints(&mut bitmap, &hints) else {
        return Ok(None);
    };

    let text = result.getText().trim();
    if text.is_empty() {
        return Ok(None);
    }

    Ok(Some(BarcodeScanResult {
        text: text.to_string(),
        format: barcode_format_id(*result.getBarcodeFormat()).to_string(),
    }))
}

fn legacy_scan_formats() -> HashSet<BarcodeFormat> {
    HashSet::from([
        BarcodeFormat::QR_CODE,
        BarcodeFormat::AZTEC,
        BarcodeFormat::DATA_MATRIX,
        BarcodeFormat::PDF_417,
        BarcodeFormat::CODE_128,
        BarcodeFormat::CODE_39,
        BarcodeFormat::CODE_93,
        BarcodeFormat::CODABAR,
        BarcodeFormat::ITF,
        BarcodeFormat::EAN_13,
        BarcodeFormat::EAN_8,
        BarcodeFormat::UPC_A,
        BarcodeFormat::UPC_E,
    ])
}

fn middle_band(image: &GrayImage) -> Option<GrayImage> {
    let width = image.width();
    let height = image.height();
    let band_y = height / 3;
    let band_height = 32_u32.max(height / 3).min(height.saturating_sub(band_y));
    if width <= 20 || band_height <= 20 {
        return None;
    }

    Some(imageops::crop_imm(image, 0, band_y, width, band_height).to_image())
}

fn threshold_gray(image: &GrayImage, threshold: u8) -> GrayImage {
    ImageBuffer::from_fn(image.width(), image.height(), |x, y| {
        if image.get_pixel(x, y).0[0] >= threshold {
            Luma([u8::MAX])
        } else {
            Luma([0])
        }
    })
}

fn scale_gray_nearest_2x(image: &GrayImage) -> Result<GrayImage, BarcodeScanError> {
    let width = image
        .width()
        .checked_mul(2)
        .ok_or(BarcodeScanError::ImageTooLarge)?;
    let height = image
        .height()
        .checked_mul(2)
        .ok_or(BarcodeScanError::ImageTooLarge)?;
    checked_pixel_len(width, height)?;
    Ok(imageops::resize(
        image,
        width,
        height,
        imageops::FilterType::Nearest,
    ))
}

fn checked_pixel_len(width: u32, height: u32) -> Result<usize, BarcodeScanError> {
    width
        .checked_mul(height)
        .and_then(|pixels| usize::try_from(pixels).ok())
        .ok_or(BarcodeScanError::ImageTooLarge)
}

fn barcode_format_id(format: BarcodeFormat) -> &'static str {
    match format {
        BarcodeFormat::AZTEC => "AZTEC",
        BarcodeFormat::CODABAR => "CODABAR",
        BarcodeFormat::CODE_39 => "CODE_39",
        BarcodeFormat::CODE_93 => "CODE_93",
        BarcodeFormat::CODE_128 => "CODE_128",
        BarcodeFormat::DATA_MATRIX => "DATA_MATRIX",
        BarcodeFormat::EAN_8 => "EAN_8",
        BarcodeFormat::EAN_13 => "EAN_13",
        BarcodeFormat::ITF => "ITF",
        BarcodeFormat::PDF_417 => "PDF_417",
        BarcodeFormat::QR_CODE => "QR_CODE",
        BarcodeFormat::UPC_A => "UPC_A",
        BarcodeFormat::UPC_E => "UPC_E",
        _ => "UNKNOWN",
    }
}

pub fn humanize_barcode_format(format: &str) -> String {
    match format.trim().to_ascii_uppercase().as_str() {
        "AZTEC" => "Aztec".into(),
        "CODABAR" => "Codabar".into(),
        "CODE_39" => "Code 39".into(),
        "CODE_93" => "Code 93".into(),
        "CODE_128" => "Code 128".into(),
        "DATA_MATRIX" => "Data Matrix".into(),
        "EAN_8" => "EAN-8".into(),
        "EAN_13" => "EAN-13".into(),
        "ITF" => "ITF".into(),
        "PDF_417" => "PDF417".into(),
        "QR_CODE" => "QR Code".into(),
        "UPC_A" => "UPC-A".into(),
        "UPC_E" => "UPC-E".into(),
        "" => "Barcode".into(),
        other => other.replace('_', " "),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use rxing::{qrcode::QRCodeWriter, BarcodeFormat, Writer};

    #[test]
    fn decodes_generated_qr_code() {
        let writer = QRCodeWriter {};
        let matrix = writer
            .encode(
                "https://oddsnap.local/scan",
                &BarcodeFormat::QR_CODE,
                96,
                96,
            )
            .expect("encode qr");
        let image = matrix_to_gray_image(&matrix);

        let result = decode_barcode_dynamic_image(&DynamicImage::ImageLuma8(image))
            .expect("decode should not fail")
            .expect("qr should decode");

        assert_eq!(result.text, "https://oddsnap.local/scan");
        assert_eq!(result.format, "QR_CODE");
    }

    #[test]
    fn humanizes_legacy_format_ids() {
        assert_eq!(humanize_barcode_format("QR_CODE"), "QR Code");
        assert_eq!(humanize_barcode_format("CODE_128"), "Code 128");
        assert_eq!(humanize_barcode_format(""), "Barcode");
    }

    fn matrix_to_gray_image(matrix: &rxing::common::BitMatrix) -> GrayImage {
        ImageBuffer::from_fn(matrix.getWidth(), matrix.getHeight(), |x, y| {
            if matrix.get(x, y) {
                Luma([0])
            } else {
                Luma([u8::MAX])
            }
        })
    }
}
