// FocusCore sync server — single static Go binary.
//
// Endpoints:
//   GET  /health          health check
//   POST /sync            delta sync (LWW conflict resolution, soft deletes)
//
// Auth: static Bearer token from the SYNC_AUTH_TOKEN environment variable.
//
// Storage: SQLite via modernc.org/sqlite (pure Go, CGO-free). Schema is the
// shared repo-root schema.sql so there is a single source of truth.

package main

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"os"
	"strings"

	_ "modernc.org/sqlite"
)

const (
	healthPath = "/health"
	syncPath   = "/sync"
	schemaPath = "../schema.sql"
)

type server struct {
	db   *sql.DB
	token string
}

// ----- request / response types -----

type syncRequest struct {
	LastSync    int64         `json:"last_sync"`
	Tasks       []taskRow     `json:"tasks,omitempty"`
	TimeEntries []timeEntryRow `json:"time_entries,omitempty"`
}

type syncResponse struct {
	Tasks       []taskRow     `json:"tasks"`
	TimeEntries []timeEntryRow `json:"time_entries"`
}

type taskRow struct {
	ID                 string  `json:"id"`
	ParentID           *string `json:"parent_id,omitempty"`
	Title              string  `json:"title"`
	Notes              string  `json:"notes"`
	Status             string  `json:"status"`
	ScheduledDate      *string `json:"scheduled_date,omitempty"`
	TimeboxStartMin    *int    `json:"timebox_start_min,omitempty"`
	TimeboxDurationMin *int    `json:"timebox_duration_min,omitempty"`
	ReminderEnabled    int     `json:"reminder_enabled"`
	SortOrder          int     `json:"sort_order"`
	CreatedAt          int64   `json:"created_at"`
	UpdatedAt          int64   `json:"updated_at"`
	DeletedAt          *int64  `json:"deleted_at,omitempty"`
	IsDirty            int     `json:"is_dirty"`
}

type timeEntryRow struct {
	ID         string  `json:"id"`
	TaskID     string  `json:"task_id"`
	Mode       string  `json:"mode"`
	StartedAt  int64   `json:"started_at"`
	EndedAt    int64   `json:"ended_at"`
	DurationSec int    `json:"duration_sec"`
	CreatedAt  int64   `json:"created_at"`
	UpdatedAt  int64   `json:"updated_at"`
	DeletedAt  *int64  `json:"deleted_at,omitempty"`
	IsDirty    int     `json:"is_dirty"`
}

// ----- helpers -----

func nullString(s *string) sql.NullString {
	if s == nil {
		return sql.NullString{Valid: false}
	}
	return sql.NullString{String: *s, Valid: true}
}

func nullInt64(i *int64) sql.NullInt64 {
	if i == nil {
		return sql.NullInt64{Valid: false}
	}
	return sql.NullInt64{Int64: *i, Valid: true}
}

func scanNullString(ns sql.NullString) *string {
	if !ns.Valid {
		return nil
	}
	return &ns.String
}

func scanNullInt64(ni sql.NullInt64) *int64 {
	if !ni.Valid {
		return nil
	}
	return &ni.Int64
}

func authOK(r *http.Request, token string) bool {
	if token == "" {
		// No token configured: allow local dev, but still require header presence.
		return true
	}
	h := r.Header.Get("Authorization")
	const bearer = "Bearer "
	return strings.HasPrefix(h, bearer) && strings.TrimSpace(h[len(bearer):]) == token
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}

// ----- schema -----

func applySchema(db *sql.DB) error {
	data, err := os.ReadFile(schemaPath)
	if err != nil {
		return fmt.Errorf("read schema: %w", err)
	}
	_, err = db.Exec(string(data))
	if err != nil {
		return fmt.Errorf("apply schema: %w", err)
	}
	return nil
}

// ----- handlers -----

func (s *server) health(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

func (s *server) sync(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	if !authOK(r, s.token) {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "unauthorized"})
		return
	}

	var req syncRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "invalid request body"})
		return
	}

	tx, err := s.db.Begin()
	if err != nil {
		http.Error(w, "begin tx", http.StatusInternalServerError)
		return
	}
	defer tx.Rollback()

	if err := pushTasks(tx, req.Tasks); err != nil {
		http.Error(w, "push tasks: "+err.Error(), http.StatusInternalServerError)
		return
	}
	if err := pushTimeEntries(tx, req.TimeEntries); err != nil {
		http.Error(w, "push time entries: "+err.Error(), http.StatusInternalServerError)
		return
	}
	if err := tx.Commit(); err != nil {
		http.Error(w, "commit", http.StatusInternalServerError)
		return
	}

	tasks, err := pullTasks(s.db, req.LastSync)
	if err != nil {
		http.Error(w, "pull tasks: "+err.Error(), http.StatusInternalServerError)
		return
	}
	entries, err := pullTimeEntries(s.db, req.LastSync)
	if err != nil {
		http.Error(w, "pull time entries: "+err.Error(), http.StatusInternalServerError)
		return
	}

	writeJSON(w, http.StatusOK, syncResponse{
		Tasks:       tasks,
		TimeEntries: entries,
	})
}

// pushTasks applies client dirty rows using Last-Write-Wins (updated_at ms).
func pushTasks(tx *sql.Tx, rows []taskRow) error {
	for _, row := range rows {
		var existing sql.NullInt64
		err := tx.QueryRow(`SELECT updated_at FROM tasks WHERE id = ?`, row.ID).Scan(&existing)
		if err != nil && !errors.Is(err, sql.ErrNoRows) {
			return err
		}
		if existing.Valid && existing.Int64 >= row.UpdatedAt {
			// Server is newer or equal: keep server, clear dirty flag.
			if _, err := tx.Exec(`UPDATE tasks SET is_dirty = 0 WHERE id = ?`, row.ID); err != nil {
				return err
			}
			continue
		}
		if existing.Valid {
			if _, err := tx.Exec(`
				UPDATE tasks SET
					parent_id = ?, title = ?, notes = ?, status = ?,
					scheduled_date = ?, timebox_start_min = ?, timebox_duration_min = ?,
					reminder_enabled = ?, sort_order = ?, created_at = ?,
					updated_at = ?, deleted_at = ?, is_dirty = 0
				WHERE id = ?`,
				nullString(row.ParentID), row.Title, row.Notes, row.Status,
				nullString(row.ScheduledDate), nullPtrInt(row.TimeboxStartMin), nullPtrInt(row.TimeboxDurationMin),
				row.ReminderEnabled, row.SortOrder, row.CreatedAt, row.UpdatedAt,
				nullInt64(row.DeletedAt), row.ID); err != nil {
				return err
			}
		} else {
			if _, err := tx.Exec(`
				INSERT INTO tasks
				(id, parent_id, title, notes, status, scheduled_date, timebox_start_min,
				 timebox_duration_min, reminder_enabled, sort_order, created_at, updated_at,
				 deleted_at, is_dirty)
				VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)`,
				row.ID, nullString(row.ParentID), row.Title, row.Notes, row.Status,
				nullString(row.ScheduledDate), nullPtrInt(row.TimeboxStartMin), nullPtrInt(row.TimeboxDurationMin),
				row.ReminderEnabled, row.SortOrder, row.CreatedAt, row.UpdatedAt,
				nullInt64(row.DeletedAt)); err != nil {
				return err
			}
		}
	}
	return nil
}

func nullPtrInt(i *int) sql.NullInt64 {
	if i == nil {
		return sql.NullInt64{Valid: false}
	}
	return sql.NullInt64{Int64: int64(*i), Valid: true}
}

func pushTimeEntries(tx *sql.Tx, rows []timeEntryRow) error {
	for _, row := range rows {
		var existing sql.NullInt64
		err := tx.QueryRow(`SELECT updated_at FROM time_entries WHERE id = ?`, row.ID).Scan(&existing)
		if err != nil && !errors.Is(err, sql.ErrNoRows) {
			return err
		}
		if existing.Valid && existing.Int64 >= row.UpdatedAt {
			if _, err := tx.Exec(`UPDATE time_entries SET is_dirty = 0 WHERE id = ?`, row.ID); err != nil {
				return err
			}
			continue
		}
		if existing.Valid {
			if _, err := tx.Exec(`
				UPDATE time_entries SET
					task_id = ?, mode = ?, started_at = ?, ended_at = ?, duration_sec = ?,
					created_at = ?, updated_at = ?, deleted_at = ?, is_dirty = 0
				WHERE id = ?`,
				row.TaskID, row.Mode, row.StartedAt, row.EndedAt, row.DurationSec,
				row.CreatedAt, row.UpdatedAt, nullInt64(row.DeletedAt), row.ID); err != nil {
				return err
			}
		} else {
			if _, err := tx.Exec(`
				INSERT INTO time_entries
				(id, task_id, mode, started_at, ended_at, duration_sec, created_at, updated_at, deleted_at, is_dirty)
				VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 0)`,
				row.ID, row.TaskID, row.Mode, row.StartedAt, row.EndedAt, row.DurationSec,
				row.CreatedAt, row.UpdatedAt, nullInt64(row.DeletedAt)); err != nil {
				return err
			}
		}
	}
	return nil
}

func pullTasks(db *sql.DB, lastSync int64) ([]taskRow, error) {
	rows, err := db.Query(`
		SELECT id, parent_id, title, notes, status, scheduled_date, timebox_start_min,
			timebox_duration_min, reminder_enabled, sort_order, created_at, updated_at, deleted_at
		FROM tasks WHERE updated_at > ? ORDER BY updated_at`, lastSync)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []taskRow
	for rows.Next() {
		var r taskRow
		var pid, sd sql.NullString
		var tsm, tdm sql.NullInt64
		var da sql.NullInt64
		if err := rows.Scan(&r.ID, &pid, &r.Title, &r.Notes, &r.Status, &sd,
			&tsm, &tdm, &r.ReminderEnabled, &r.SortOrder, &r.CreatedAt, &r.UpdatedAt, &da); err != nil {
			return nil, err
		}
		r.ParentID = scanNullString(pid)
		r.ScheduledDate = scanNullString(sd)
		if tsm.Valid { v := int(tsm.Int64); r.TimeboxStartMin = &v }
		if tdm.Valid { v := int(tdm.Int64); r.TimeboxDurationMin = &v }
		r.DeletedAt = scanNullInt64(da)
		out = append(out, r)
	}
	return out, rows.Err()
}

func pullTimeEntries(db *sql.DB, lastSync int64) ([]timeEntryRow, error) {
	rows, err := db.Query(`
		SELECT id, task_id, mode, started_at, ended_at, duration_sec, created_at, updated_at, deleted_at
		FROM time_entries WHERE updated_at > ? ORDER BY updated_at`, lastSync)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []timeEntryRow
	for rows.Next() {
		var r timeEntryRow
		var da sql.NullInt64
		if err := rows.Scan(&r.ID, &r.TaskID, &r.Mode, &r.StartedAt, &r.EndedAt, &r.DurationSec,
			&r.CreatedAt, &r.UpdatedAt, &da); err != nil {
			return nil, err
		}
		r.DeletedAt = scanNullInt64(da)
		out = append(out, r)
	}
	return out, rows.Err()
}

func (s *server) routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc(healthPath, s.health)
	mux.HandleFunc(syncPath, s.sync)
	return mux
}

func main() {
	token := os.Getenv("SYNC_AUTH_TOKEN")
	dbPath := os.Getenv("FOCUSCORE_DB")
	if dbPath == "" {
		dbPath = "focuscore.db"
	}

	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		log.Fatalf("open db: %v", err)
	}
	defer db.Close()

	if err := applySchema(db); err != nil {
		log.Fatalf("apply schema: %v", err)
	}

	addr := os.Getenv("FOCUSCORE_ADDR")
	if addr == "" {
		addr = ":8080"
	}

	s := &server{db: db, token: token}
	log.Printf("focuscore sync server listening on %s", addr)
	if err := http.ListenAndServe(addr, s.routes()); err != nil {
		log.Fatalf("listen: %v", err)
	}
}
