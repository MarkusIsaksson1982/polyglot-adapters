<?php
declare(strict_types=1);
namespace Polyglot;

/** Process adapter: one long-lived Python worker, JSON lines over stdin/stdout (see python/redact_worker.py). */
final class PyWorker {
    private $proc; private array $pipes = []; private int $next = 1;
    public function __construct(string $script, string $python = 'python3') {
        $this->proc = proc_open([$python, $script], [0 => ['pipe', 'r'], 1 => ['pipe', 'w'], 2 => ['pipe', 'w']], $this->pipes);
        if (!is_resource($this->proc)) throw new \RuntimeException('could not start python worker');
    }
    public function call(string $op, array $args = []): mixed {
        $id = $this->next++;
        fwrite($this->pipes[0], json_encode(['id' => $id, 'op' => $op] + $args, JSON_THROW_ON_ERROR) . "\n");
        $line = fgets($this->pipes[1]);
        if ($line === false) throw new \RuntimeException('python worker closed');
        $res = json_decode($line, true, 512, JSON_THROW_ON_ERROR);
        if (($res['id'] ?? null) !== $id) throw new \RuntimeException('response id mismatch');
        if (!$res['ok']) throw new GatewayError(0, $res['error']['code'], $res['error']['message']);
        return $res['result'];
    }
    public function close(): void { if (is_resource($this->proc)) { fclose($this->pipes[0]); proc_close($this->proc); } }
    public function __destruct() { $this->close(); }
}
