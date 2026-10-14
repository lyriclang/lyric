/* The names of musl's math functions here carry the runtime's prefix (design 09 D7, 13 M10 S12):
 * the program links the system's libm too (sqrt, floor, the rest), and these must not collide
 * with it, nor be chosen over it. Included first in every unit of this directory. The fused
 * multiply-add paths are switched off: the same bits on x86-64 and arm64, and in the compiler's
 * port of the same code (Lyric.Frontend/Ir/Interp/Libm.cs). */
#ifndef LYR_NAMES_H
#define LYR_NAMES_H

/* The system header first: the renames below must not reach its own declarations. */
#include <math.h>
#include <stdint.h>

#undef __FP_FAST_FMA
#define __FP_FAST_FMA 0

#define exp lyr_exp
#define log lyr_log
#define log2 lyr_log2
#define log10 lyr_log10
#define pow lyr_pow
#define sin lyr_sin
#define cos lyr_cos
#define tan lyr_tan
#define asin lyr_asin
#define acos lyr_acos
#define atan lyr_atan
#define atan2 lyr_atan2
#define sinh lyr_sinh
#define cosh lyr_cosh
#define tanh lyr_tanh
#define cbrt lyr_cbrt
#define hypot lyr_hypot
#define expm1 lyr_expm1
#define scalbn lyr_scalbn

#define __rem_pio2_large lyr_libm_rem_pio2_large
#define __rem_pio2 lyr_libm_rem_pio2
#define __sin lyr_libm_sin
#define __cos lyr_libm_cos
#define __tan lyr_libm_tan
#define __expo2 lyr_libm_expo2
#define __math_xflow lyr_libm_math_xflow
#define __math_uflow lyr_libm_math_uflow
#define __math_oflow lyr_libm_math_oflow
#define __math_divzero lyr_libm_math_divzero
#define __math_invalid lyr_libm_math_invalid
#define __exp_data lyr_libm_exp_data
#define __log_data lyr_libm_log_data
#define __log2_data lyr_libm_log2_data
#define __pow_log_data lyr_libm_pow_log_data

/* The prototypes under the new names (the system's header declared the old ones only). */
double exp(double);
double log(double);
double log2(double);
double log10(double);
double pow(double, double);
double sin(double);
double cos(double);
double tan(double);
double asin(double);
double acos(double);
double atan(double);
double atan2(double, double);
double sinh(double);
double cosh(double);
double tanh(double);
double cbrt(double);
double hypot(double, double);
double expm1(double);
double scalbn(double, int);

#endif
