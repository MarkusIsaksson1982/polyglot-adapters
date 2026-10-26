<?php
declare(strict_types=1);
require __DIR__ . '/../src/GatewayClient.php';
require __DIR__ . '/../src/PyWorker.php';
use Polyglot\{GatewayClient, GatewayError, PyWorker};
$pass = 0; $fail = 0;
function t(string $name, callable $f): void { global $pass, $fail; try { $f(); $pass++; echo "ok   $name\n"; } catch (\Throwable $e) { $fail++; echo "FAIL $name: {$e->getMessage()}\n"; } }
function eq($a, $b): void { if ($a !== $b) throw new \Exception(var_export($a, true) . ' !== ' . var_export($b, true)); }
function throws(callable $f, string $code): void { try { $f(); } catch (GatewayError $e) { eq($e->errorCode, $code); return; } throw new \Exception("expected $code"); }
$url = getenv('GATEWAY_URL') ?: 'http://127.0.0.1:8765'; $key = getenv('GATEWAY_KEY') ?: 'test-key';
$g = new GatewayClient($url, $key);
t('health', fn() => eq($g->health()['status'] ?? null, 'ok'));
t('bad key -> typed 401', fn() => throws(fn() => (new GatewayClient($url, 'wrong'))->audit(), 'unauthorized'));
t('unknown type rejected client-side', function () use ($g) { try { $g->createJob('delete', 'x'); throw new \Exception('no'); } catch (\InvalidArgumentException) {} });
t('process redacts PII before the model', function () use ($g) {
    $j = $g->process('summarize', 'Kontakta anna@example.se, 19900101-1234');
    eq($j['status'], 'completed');
    if (str_contains($j['result'], 'anna@example.se') || str_contains($j['result'], '1234')) throw new \Exception('PII reached model: ' . $j['result']);
});
t('model failure -> job_failed', fn() => throws(fn() => $g->process('classify', 'FAIL_MODEL please'), 'job_failed'));
t('missing job -> typed 404', fn() => throws(fn() => $g->getJob(999999), 'not_found'));
t('audit has one row per run, hashes only', function () use ($g) {
    $a = $g->audit(); if (count($a) < 2) throw new \Exception('audit rows ' . count($a));
    eq(strlen($a[0]['prompt_hash']), 64);
});
t('unreachable gateway -> typed error', fn() => throws(fn() => (new GatewayClient('http://127.0.0.1:1', $key, 1.0))->health(), 'unreachable'));
$w = new PyWorker(__DIR__ . '/../../python/redact_worker.py');
t('worker ping', fn() => eq($w->call('ping'), 'pong'));
t('worker redact (Python logic from PHP)', function () use ($w) { $r = $w->call('redact', ['text' => 'mail anna@example.se']); if (str_contains($r, '@')) throw new \Exception($r); });
t('worker unknown op -> typed', fn() => throws(fn() => $w->call('rm'), 'unknown_op'));
t('worker 200 sequential calls', function () use ($w) { for ($i = 0; $i < 200; $i++) $w->call('ping'); });
echo "\nphp: $pass passed, $fail failed\n"; exit($fail ? 1 : 0);
