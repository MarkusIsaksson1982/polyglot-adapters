<?php
declare(strict_types=1);
namespace Polyglot;

/** Typed error carrying the gateway's {"code","message"} body and HTTP status. */
final class GatewayError extends \RuntimeException {
    public function __construct(public readonly int $status, public readonly string $errorCode, string $message) {
        parent::__construct("[$status $errorCode] $message");
    }
}

/**
 * Thin PHP adapter for the Python fastapi-ai-automation-gateway.
 * PHP code (an existing app, a cron job, a CMS plugin) keeps its own logic and hands AI work to Python,
 * which owns redaction, the local model call and the audit log. Stdlib only (stream contexts, no ext-curl).
 */
final class GatewayClient {
    public function __construct(private string $baseUrl, private string $apiKey, private float $timeout = 10.0) {
        $this->baseUrl = rtrim($baseUrl, '/');
    }

    public function health(): array { return $this->request('GET', '/healthz'); }
    /** @param 'summarize'|'classify'|'extract' $type */
    public function createJob(string $type, string $text): array {
        if (!in_array($type, ['summarize', 'classify', 'extract'], true)) throw new \InvalidArgumentException("unknown job type: $type");
        return $this->request('POST', '/jobs', ['type' => $type, 'input_text' => $text]);
    }
    public function runJob(int $id): array { return $this->request('POST', "/jobs/$id/run"); }
    public function getJob(int $id): array { return $this->request('GET', "/jobs/$id"); }
    public function audit(): array { return $this->request('GET', '/audit'); }
    /** Convenience: create + run, throw if the model step failed. */
    public function process(string $type, string $text): array {
        $job = $this->runJob((int)$this->createJob($type, $text)['id']);
        if ($job['status'] !== 'completed') throw new GatewayError(502, 'job_failed', (string)($job['error'] ?? 'job failed'));
        return $job;
    }

    private function request(string $method, string $path, ?array $body = null): array {
        $headers = ["X-API-Key: {$this->apiKey}", 'Accept: application/json'];
        $opts = ['method' => $method, 'timeout' => $this->timeout, 'ignore_errors' => true, 'header' => $headers];
        if ($body !== null) { $opts['content'] = json_encode($body, JSON_THROW_ON_ERROR); $opts['header'][] = 'Content-Type: application/json'; }
        $raw = @file_get_contents($this->baseUrl . $path, false, stream_context_create(['http' => $opts]));
        if ($raw === false) throw new GatewayError(0, 'unreachable', "gateway not reachable at {$this->baseUrl}");
        $status = (int)(preg_match('#^HTTP/\S+\s+(\d{3})#', $http_response_header[0] ?? '', $m) ? $m[1] : 0);
        $data = json_decode($raw, true);
        if ($status >= 400) {
            $err = $data['error'] ?? $data['detail'] ?? $data ?? [];
            throw new GatewayError($status, (string)($err['code'] ?? 'http_error'), (string)($err['message'] ?? 'request failed'));
        }
        if (!is_array($data)) throw new GatewayError($status, 'bad_response', 'non-JSON response');
        return $data;
    }
}
