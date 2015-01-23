//xfail:NOT_ALL_VERIFIED
//--local_size=1024 --num_groups=1024 --no-inline
//kernel.cl:[\s]+error:[\s]+possible write-write race on p\[[\d]+]
//Write by work item[\s]+[\d]+[\s]+in work group[\s]+[\d]+.+kernel.cl:14:(3|5):[\s]+p\[get_local_id\(0\) \+ 1] = get_global_id\(0\);
//Write by work item[\s]+[\d]+[\s]+in work group[\s]+[\d]+.+kernel.cl:10:(3|5):[\s]+p\[get_local_id\(0\)] = get_global_id\(0\);


__kernel void foo(__local int* p) {

  p[get_local_id(0)] = get_global_id(0);

  barrier(0);

  p[get_local_id(0) + 1] = get_global_id(0);
}
