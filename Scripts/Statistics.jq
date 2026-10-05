def stats:
    if length == 0 then null else
        sort as $a | length as $n | (add / $n) as $mean |
        {Count:$n, Median:(if $n % 2 == 1 then $a[($n/2|floor)] else ($a[$n/2-1]+$a[$n/2])/2 end),
         Minimum:$a[0], Maximum:$a[-1], Mean:$mean,
         StandardDeviation:(if $n > 1 then (map((.-$mean)*(.-$mean)) | add / ($n-1) | sqrt) else null end)}
    end;
.Results |= map(.Statistics = {
    Compilation:(.BuildSeconds|stats), Process:(.ProcessSeconds|stats),
    Kernel:(.KernelSeconds|stats), Memory:(.PeakMemoryBytes|stats)
})

