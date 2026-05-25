package com.endava.juniorprogram.usersprogram.springconfig;

import com.endava.juniorprogram.usersprogram.services.repositories.DatabaseRepository;
import com.endava.juniorprogram.usersprogram.services.repositories.UsersRepo;
import org.springframework.context.annotation.Configuration;

@Configuration
public class AppConfig {
    public DatabaseRepository usersRepo(){
        return new UsersRepo();
    }
}
