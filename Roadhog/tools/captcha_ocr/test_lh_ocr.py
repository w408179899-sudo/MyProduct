import json
from pathlib import Path
import sys
import tempfile
import unittest

from lh_ocr import LhOcr, OcrError, parse_result


class ResultContractTests(unittest.TestCase):
    def test_one_line_preserves_raw_output_and_case(self):
        result = parse_result(dict(status="ok", raw_text="p3TmSK\r\n", raw_details="box metadata"))
        self.assertEqual(result.raw_text, "p3TmSK\r\n")
        self.assertEqual(result.text, "p3TmSK")
        self.assertTrue(result.eligible_to_submit)

    def test_incomplete_multiline_spaces_and_unicode_never_become_six_characters(self):
        for raw in ("wLpm\r\n", "ABC\r\n123\r\n", "ABC 123\r\n", "ＡBC123\r\n", "ABC123\r\n\r\n"):
            with self.subTest(raw=raw):
                self.assertFalse(parse_result(dict(status="ok", raw_text=raw, raw_details="")).eligible_to_submit)

    def test_plugin_error_cannot_be_text_candidate(self):
        with self.assertRaises(OcrError) as raised:
            parse_result(dict(status="ok", raw_text="-88", raw_details=""))
        self.assertEqual(raised.exception.code, "license_required")


class HostLifecycleTests(unittest.TestCase):
    def host(self, body, timeout=2):
        code = 'import json,sys,time\nrequest=json.loads(sys.stdin.readline())\n' + body
        return LhOcr("unused.dll", "unused.exe", timeout_seconds=timeout,
                     _host_command=[sys.executable, "-u", "-c", code])

    def test_registration_failure_stops_before_any_image_request(self):
        code = 'print(\'LH_OCR_JSON:{"status":"error","code":"license_required","plugin_code":-88}\',flush=True)\n'
        with self.assertRaises(OcrError) as raised:
            self.host(code)
        self.assertEqual(raised.exception.plugin_code, -88)

    def test_persistent_host_recognizes_twice_then_closes(self):
        body = '''print('LH_OCR_JSON:{"status":"ready","host_bits":32,"engine":"lh.dll"}',flush=True)
for line in sys.stdin:
    request=json.loads(line)
    if request['op']=='shutdown':break
    print('LH_OCR_JSON:'+json.dumps(dict(status='ok',raw_text='P3TMSK\\r\\n',raw_details='')),flush=True)
'''
        with self.host(body) as host:
            for _ in range(2):
                self.assertEqual(host.classification(b"image bytes"), "P3TMSK")
            process = host._process
        self.assertIsNotNone(process.poll())

    def test_timeout_terminates_owned_worker(self):
        body = '''print('LH_OCR_JSON:{"status":"ready","host_bits":32,"engine":"lh.dll"}',flush=True)
sys.stdin.readline()
time.sleep(30)
'''
        host = self.host(body, timeout=0.15)
        with self.assertRaises(OcrError) as raised:
            host.classification(b"image bytes")
        self.assertEqual(raised.exception.code, "host_timeout")
        self.assertIsNotNone(host._process.poll())

    def test_invalid_host_protocol_fails_without_fallback(self):
        with self.assertRaises(OcrError) as raised:
            self.host("print('LH_OCR_JSON:not-json',flush=True)\n")
        self.assertEqual(raised.exception.code, "invalid_host_response")

    def test_config_rejects_persisted_registration_secret(self):
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory)/"config.json"
            config.write_text(json.dumps(dict(dll_path="x",host_path="x",registration_code="do-not-save")))
            with self.assertRaises(OcrError) as raised:
                LhOcr.from_config(config)
            self.assertEqual(raised.exception.code, "registration_secret_must_use_environment")


if __name__ == "__main__":
    unittest.main()
