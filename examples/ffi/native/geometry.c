/* A small C library the example binds: it lies in the package's native part, so `lyric build`
 * compiles it and links it into the program. */
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

typedef struct {
    double x;
    double y;
} Vec;

/* a struct by value, and behind a pointer C writes through */
double vec_length(Vec v) { return sqrt(v.x * v.x + v.y * v.y); }

void vec_scale(Vec *v, double k) {
    v->x *= k;
    v->y *= k;
}

/* a string in, one out: the bytes are C's, valid until the next call */
static char label[96];

const char *vec_label(const char *name, Vec v) {
    snprintf(label, sizeof label, "%s(%.1f, %.1f)", name, v.x, v.y);
    return label;
}

/* a callback with its userdata: f for each corner of a box, clockwise from the origin */
void each_corner(double w, double h, void (*f)(void *userdata, double x, double y), void *userdata) {
    f(userdata, 0, 0);
    f(userdata, 0, h);
    f(userdata, w, h);
    f(userdata, w, 0);
}

/* a handle C owns: a path of points, made, grown, measured and freed by C */
typedef struct Path {
    Vec *points;
    int64_t count, room;
} Path;

Path *path_new(void) { return calloc(1, sizeof(Path)); }

void path_add(Path *p, double x, double y) {
    if (p->count == p->room) {
        p->room = p->room == 0 ? 4 : p->room * 2;
        p->points = realloc(p->points, (size_t)p->room * sizeof(Vec));
    }
    p->points[p->count++] = (Vec){ x, y };
}

double path_length(const Path *p) {
    double total = 0;
    for (int64_t i = 1; i < p->count; i++) {
        Vec d = { p->points[i].x - p->points[i - 1].x, p->points[i].y - p->points[i - 1].y };
        total += vec_length(d);
    }
    return total;
}

void path_free(Path *p) {
    free(p->points);
    free(p);
}
