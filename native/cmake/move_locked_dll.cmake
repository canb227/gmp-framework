# Runs before linking gmp_box3d. A game started from the editor keeps the dll open, so the
# linker can't overwrite it; Windows still lets a loaded dll be renamed, so move it aside
# and the running game keeps the old copy while the next launch gets the new one.
# Usage: cmake -DDLL=<path to the dll> -P move_locked_dll.cmake

# Copies moved aside earlier, deleted once nothing has them open any more.
file(GLOB OLD_COPIES "${DLL}.*.old")
foreach(OLD_COPY ${OLD_COPIES})
    file(REMOVE "${OLD_COPY}")
endforeach()

if(EXISTS "${DLL}")
    string(TIMESTAMP NOW "%Y%m%d%H%M%S")
    file(RENAME "${DLL}" "${DLL}.${NOW}.old" RESULT RENAME_RESULT)
    # Gone straight away unless something has it open.
    file(REMOVE "${DLL}.${NOW}.old")
endif()
