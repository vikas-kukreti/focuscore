package main

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"database/sql"

	_ "modernc.org/sqlite"
)

const testToken = "test-token-123"

// newTestServer opens a fresh temp SQLite DB, applies the shared schema, and
// returns an httptest server serving the routes with bearer auth enabled.
func newTestServer(t *testing.T) *httptest.Server {
	t.Helper()
	dbPath := t.TempDir() + "/focuscore.db"
	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		t.Fatalf("open db: %v", err)
	}
	s := &server{db: db, token: testToken}
	if err := applySchema(db); err != nil {
		t.Fatalf("apply schema: %v", err)
	}
	return httptest.NewServer(s.routes())
}

func postSync(t *testing.T, srv *httptest.Server, body any, token string) (*http.Response, []byte) {
	t.Helper()
	var b []byte
	if body != nil {
		var err error
		b, err = json.Marshal(body)
		if err != nil {
			t.Fatalf("marshal: %v", err)
		}
	}
	req, err := http.NewRequest(http.MethodPost, srv.URL+"/sync", bytes.NewReader(b))
	if err != nil {
		t.Fatal(err)
	}
	if token != "" {
		req.Header.Set("Authorization", "Bearer "+token)
	}
	req.Header.Set("Content-Type", "application/json")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		t.Fatalf("do: %v", err)
	}
	defer resp.Body.Close()
	out, err := io.ReadAll(resp.Body)
	if err != nil {
		t.Fatalf("read: %v", err)
	}
	return resp, out
}

func unmarshalSync(t *testing.T, out []byte) syncResponse {
	t.Helper()
	var sr syncResponse
	if err := json.Unmarshal(out, &sr); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	return sr
}

func TestHealth(t *testing.T) {
	srv := newTestServer(t)
	defer srv.Close()
	resp, err := http.Get(srv.URL + "/health")
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("health status = %d, want 200", resp.StatusCode)
	}
}

func TestSyncRequiresAuth(t *testing.T) {
	srv := newTestServer(t)
	defer srv.Close()
	resp, _ := postSync(t, srv, &syncRequest{}, "")
	if resp.StatusCode != http.StatusUnauthorized {
		t.Fatalf("unauthed sync = %d, want 401", resp.StatusCode)
	}
}

func TestSyncPushAndPullRoundTrip(t *testing.T) {
	srv := newTestServer(t)
	defer srv.Close()

	now := time.Now().UnixMilli()
	task := taskRow{
		ID:             "task-1",
		Title:          "Write docs",
		Notes:          "draft the PRD",
		Status:         "todo",
		SortOrder:      1,
		CreatedAt:      now,
		UpdatedAt:      now,
		ReminderEnabled: 1,
	}

	resp, out := postSync(t, srv, &syncRequest{LastSync: 0, Tasks: []taskRow{task}}, testToken)
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("push sync = %d, want 200", resp.StatusCode)
	}
	sr := unmarshalSync(t, out)
	if len(sr.Tasks) != 1 || sr.Tasks[0].ID != "task-1" {
		t.Fatalf("expected pushed task back, got %+v", sr.Tasks)
	}

	// Sync again with last_sync beyond updated_at -> nothing new.
	resp2, out2 := postSync(t, srv, &syncRequest{LastSync: now + 1}, testToken)
	if resp2.StatusCode != http.StatusOK {
		t.Fatalf("second sync = %d", resp2.StatusCode)
	}
	sr2 := unmarshalSync(t, out2)
	if len(sr2.Tasks) != 0 {
		t.Fatalf("expected empty after last_sync, got %d tasks", len(sr2.Tasks))
	}
}

func TestSyncLWWServerWins(t *testing.T) {
	srv := newTestServer(t)
	defer srv.Close()

	now := time.Now().UnixMilli()
	// Seed server with a newer version by pushing first.
	seed := taskRow{ID: "task-lww", Title: "server version", UpdatedAt: now + 1000}
	if _, out := postSync(t, srv, &syncRequest{LastSync: 0, Tasks: []taskRow{seed}}, testToken); out == nil {
		t.Fatal("no response")
	}

	// Client pushes an older version -> server should win.
	old := taskRow{ID: "task-lww", Title: "client stale version", UpdatedAt: now}
	postSync(t, srv, &syncRequest{LastSync: 0, Tasks: []taskRow{old}}, testToken)

	resp, out := postSync(t, srv, &syncRequest{LastSync: 0}, testToken)
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("sync status = %d, want 200", resp.StatusCode)
	}
	sr := unmarshalSync(t, out)
	if len(sr.Tasks) != 1 || sr.Tasks[0].Title != "server version" {
		t.Fatalf("LWW: expected server version, got %+v", sr.Tasks)
	}
}

func TestSyncLWWClientWins(t *testing.T) {
	srv := newTestServer(t)
	defer srv.Close()

	now := time.Now().UnixMilli()
	seed := taskRow{ID: "task-lww2", Title: "server old", UpdatedAt: now}
	postSync(t, srv, &syncRequest{LastSync: 0, Tasks: []taskRow{seed}}, testToken)

	// Client pushes newer -> client wins.
	fresh := taskRow{ID: "task-lww2", Title: "client fresh", UpdatedAt: now + 5000}
	postSync(t, srv, &syncRequest{LastSync: 0, Tasks: []taskRow{fresh}}, testToken)

	resp, out := postSync(t, srv, &syncRequest{LastSync: 0}, testToken)
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("sync status = %d, want 200", resp.StatusCode)
	}
	sr := unmarshalSync(t, out)
	if len(sr.Tasks) != 1 || sr.Tasks[0].Title != "client fresh" {
		t.Fatalf("LWW: expected client fresh, got %+v", sr.Tasks)
	}
}

func TestSyncSoftDeletePropagates(t *testing.T) {
	srv := newTestServer(t)
	defer srv.Close()

	now := time.Now().UnixMilli()
	delAt := now + 100
	deleted := taskRow{ID: "task-del", Title: "gone", UpdatedAt: now, DeletedAt: &delAt}
	postSync(t, srv, &syncRequest{LastSync: 0, Tasks: []taskRow{deleted}}, testToken)

	resp, out := postSync(t, srv, &syncRequest{LastSync: 0}, testToken)
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("sync status = %d, want 200", resp.StatusCode)
	}
	sr := unmarshalSync(t, out)
	if len(sr.Tasks) != 1 || sr.Tasks[0].DeletedAt == nil || *sr.Tasks[0].DeletedAt != delAt {
		t.Fatalf("expected soft-deleted task with deleted_at, got %+v", sr.Tasks)
	}
}
